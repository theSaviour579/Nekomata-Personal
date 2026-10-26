using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian;
using Nekomata.Core.Integrations;
using Nekomata.Models.Planning;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;

// "What did I miss?" - one catch-up across everything since the user last looked, instead of each source reporting on
// its own. "Last looked" is the last catch-up, welcome back or meeting catch-up today (else the start of the working day).
// Ranked: managers on Teams; critical or high items in "Needs you"; people waiting on the user (Teams asks, unread email
// from people); then a count of the rest and the next meeting. The full list goes on screen; the spoken version is the
// top few.
//
// After a meeting with other people (25 minutes or longer) ends, the same catch-up runs for the time they were in it -
// but only speaks if something there is worth it, and never when they're away from the desk (welcome back covers that).
public partial class MainViewModel
{
    private DateTimeOffset? _lastCaughtUpAt;
    private readonly HashSet<string> _meetingCatchUpsDone = [];
    private bool _catchingUp;

    private void MarkCaughtUp() => _lastCaughtUpAt = DateTimeOffset.Now;

    private DateTimeOffset CatchUpSince()
    {
        var dayStart = new DateTimeOffset(_services.GetRequiredService<WorkingDaySettings>().GetStart(DateTime.Today));
        var since = _lastCaughtUpAt is { } last && last.Date == DateTime.Today ? last : dayStart;
        return since > DateTimeOffset.Now ? DateTimeOffset.Now.AddHours(-1) : since;
    }

    private void MaybeCatchUpAfterMeeting(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        if (_awaySince is not null || _catchingUp || !_initialLoadCompleted) return;
        var ended = events.LastOrDefault(e => !e.IsAllDay && !e.IsNekomataManaged && e.Attendees.Count > 0 &&
                                              e.End - e.Start >= TimeSpan.FromMinutes(25) && e.End <= now && now - e.End < TimeSpan.FromMinutes(3));
        if (ended is null || !_meetingCatchUpsDone.Add($"{ended.Id}:{ended.End:O}")) return;
        _ = WhatDidIMissAsync(ended.Start, onDemand: false, label: CleanCalendarObjectiveTitle(ended.Subject));
    }

    private async Task WhatDidIMissAsync(DateTimeOffset? from = null, bool onDemand = true, string label = "")
    {
        if (_catchingUp) return;
        _catchingUp = true;
        try
        {
            var since = from ?? CatchUpSince();
            if (onDemand) Think($"Looking at everything since {since.LocalDateTime:HH:mm}");
            if (_teamsLastPoll is null || DateTimeOffset.Now - _teamsLastPoll > TimeSpan.FromMinutes(3)) await RefreshTeamsAsync(force: true);

            // Needs you (Teams has its own section below).
            var needs = VisibleAttentionItems
                .Where(x => x.CreatedAt >= since && !x.Key.StartsWith("teams:", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => SeverityRank(x.Severity)).ThenByDescending(x => x.CreatedAt).ToList();
            var urgentNeeds = needs.Where(x => SeverityRank(x.Severity) >= SeverityRank("High")).ToList();

            // Teams.
            var teams = TeamsUpdates.Where(u => !u.Dismissed && !u.Replied && u.At >= since).ToList();
            var teamsManagers = teams.Where(u => u.FromManager).OrderByDescending(u => u.At).ToList();
            var teamsAsks = teams.Where(u => !u.FromManager && IsTeamsAsk(u)).OrderByDescending(u => u.Urgent).ThenByDescending(u => u.At).ToList();
            var teamsNotable = teams.Where(u => !IsTeamsAsk(u) && u.Kind is "problem" or "change" or "decision" && !TeamsConfig.MutedChats.Contains(u.ChatId)).ToList();
            var teamsOther = teams.Count(u => !IsTeamsAsk(u) && !TeamsConfig.MutedChats.Contains(u.ChatId)) - teamsNotable.Count;

            // Email from people.
            var mail = await SafeAsync(async () => (IReadOnlyList<EmailMessage>)(await _services.GetRequiredService<IEmailService>().GetUnreadInboxAsync(40))
                .Where(m => m.ReceivedAt >= since && !InboxTriageText.IsLikelyNoise(m.SenderAddress, m.SenderName, m.Subject))
                .OrderByDescending(m => m.Importance.Equals("high", StringComparison.OrdinalIgnoreCase)).ThenByDescending(m => m.ReceivedAt).ToList(),
                (IReadOnlyList<EmailMessage>)Array.Empty<EmailMessage>());

            var now = DateTimeOffset.Now;
            var nextMeeting = CalendarEvents.Where(e => !e.IsAllDay && !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start > now && e.Start - now <= TimeSpan.FromHours(1))
                .OrderBy(e => e.Start).FirstOrDefault();

            var anything = needs.Count + teams.Count + mail.Count > 0;
            var worthSpeaking = teamsManagers.Count + urgentNeeds.Count + teamsAsks.Count > 0 ||
                                mail.Any(m => m.Importance.Equals("high", StringComparison.OrdinalIgnoreCase));
            // A label starting "the " is a break ("the weekend", "the bank holiday"), not a meeting.
            var isBreak = label.StartsWith("the ", StringComparison.OrdinalIgnoreCase);
            var period = isBreak ? $"over {label}" : label.Length > 0 ? $"while you were in {label}" : $"since {since.LocalDateTime:HH:mm}";
            if (!anything)
            {
                if (onDemand || isBreak) await RespondAndSpeakAsync($"Nothing's come in {period} that needs you.");
                return;
            }
            if (!onDemand && !isBreak && !worthSpeaking) return;

            // On screen: everything.
            var sb = new StringBuilder($"CATCH-UP · {period.ToUpperInvariant()}\n");
            void Section(string title, IEnumerable<string> lines)
            {
                var list = lines.ToList();
                if (list.Count == 0) return;
                sb.Append('\n').AppendLine(title);
                foreach (var line in list) sb.AppendLine($"• {line}");
            }
            Section("MANAGERS ON TEAMS", teamsManagers.Select(u => $"{TeamsWhen(u.At)} · {u.Summary}"));
            Section("NEEDS YOU", needs.Take(8).Select(x => x.Title));
            Section("WAITING ON YOU · TEAMS", teamsAsks.Select(u => $"{TeamsWhen(u.At)} · {(u.OneToOne ? u.Sender : u.ChatName)}: {u.Summary}"));
            Section("EMAIL FROM PEOPLE", mail.Take(8).Select(m => $"{(m.ReceivedAt.LocalDateTime.Date == DateTime.Today ? m.ReceivedAt.LocalDateTime.ToString("HH:mm") : m.ReceivedAt.LocalDateTime.ToString("ddd HH:mm"))} · {m.SenderName}: {m.Subject}" +
                                                                (m.Importance.Equals("high", StringComparison.OrdinalIgnoreCase) ? " (high importance)" : "")));
            Section("ON TEAMS", teamsNotable.Select(u => $"{TeamsWhen(u.At)} · {u.Summary}")
                .Concat(teamsOther > 0 ? new[] { $"{teamsOther} other update{(teamsOther == 1 ? "" : "s")} on Home" } : Array.Empty<string>()));
            if (nextMeeting is not null) sb.Append('\n').AppendLine($"NEXT · {nextMeeting.Start.LocalDateTime:HH:mm} {CleanCalendarObjectiveTitle(nextMeeting.Subject)}");
            var text = sb.ToString().TrimEnd();
            if (onDemand) ShowPaletteResponse(text);
            else GuardianResponse = text;

            // Spoken: the top few.
            var said = new List<string> { isBreak ? $"Over {label}:" : label.Length > 0 ? $"While you were in {label}:" : $"Since {since.LocalDateTime:HH:mm}:" };
            if (teamsManagers.Count > 0) said.Add($"{TeamsSignals.FirstName(teamsManagers[0].Sender)} messaged you. {SpokenTeamsSummary(teamsManagers[0])}");
            if (urgentNeeds.Count > 0) said.Add($"{SpokenTitle(urgentNeeds[0].Title)}.");
            var waiting = teamsAsks.Select(u => TeamsSignals.FirstName(u.Sender))
                .Concat(mail.Select(m => TeamsSignals.FirstName(m.SenderName))).Where(n => n.Length > 0).Distinct().Take(4).ToList();
            if (waiting.Count > 0) said.Add($"{JoinNames(waiting)} {(waiting.Count == 1 ? "is" : "are")} waiting on you.");
            var rest = needs.Count - urgentNeeds.Count + teamsNotable.Count + Math.Max(0, teamsOther);
            if (rest > 0 && said.Count < 5) said.Add($"Plus {rest} smaller thing{(rest == 1 ? "" : "s")} on screen.");
            if (nextMeeting is not null && nextMeeting.Start - now <= TimeSpan.FromMinutes(20))
                said.Add($"{CleanCalendarObjectiveTitle(nextMeeting.Subject)} starts at {nextMeeting.Start.LocalDateTime:HH:mm}.");
            await SpeakGuardianAsync(string.Join(" ", said));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Catch-up failed safely: {ex.Message}");
            if (onDemand) await RespondAndSpeakAsync("I couldn't pull the catch-up together just now.");
        }
        finally
        {
            MarkCaughtUp();
            _catchingUp = false;
        }
    }
}
