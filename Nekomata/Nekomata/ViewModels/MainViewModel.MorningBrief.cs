using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian;
using Nekomata.Core.Integrations;
using Nekomata.Core.Operations;
using Nekomata.Core.Planning;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// The start-up briefing. Spoken in well under a minute and shown in full in the Guardian panel:
//   1. greeting and the weather (rain at home time if likely), and where the user is;
//   2. what came in since the end of the last working day (over the weekend or bank holiday after a break): manager
//      messages, urgent Needs-you items, email from people;
//   3. the day: meetings (first one, total hours) and the longest clear run for focus;
//   4. what's due: promises, tasks, duties, approvals waiting;
//   5. one suggestion for where to start.
// Out of hours it's a two-line version that only mentions anything urgent. Each section is skipped when empty.
public partial class MainViewModel
{
    private sealed record MorningBriefText(string Screen, string Spoken, bool OffHours, string BreakName);

    private async Task SpeakMorningBriefAsync()
    {
        try
        {
            var brief = await ComposeMorningBriefAsync(ignoreOffHours: false);
            GuardianResponse = brief.Screen;
            if (!brief.OffHours)
            {
                GuardianPanelExpanded = true;
                if (brief.BreakName.Length > 0)
                {
                    // This briefing covers the break; don't repeat it with a separate catch-up.
                    OffHoursSettings.LastBreakCatchUp = DateTime.Today;
                    SaveOffHoursSettings();
                }
            }
            await SpeakGuardianAsync(brief.Spoken);
            Beat("brief", true);
        }
        catch (Exception ex)
        {
            Beat("brief", false, $"fell back to the simple briefing: {ex.GetBaseException().Message}");
            System.Diagnostics.Debug.WriteLine($"Morning brief failed, using the simple one: {ex.Message}");
            await SpeakMorningBriefingAsync();
        }
    }

    /// <summary>Builds the briefing (screen text and the spoken version) without showing or saying it. The web digest uses
    /// it with <paramref name="ignoreOffHours"/> so the phone always gets the full picture.</summary>
    private async Task<MorningBriefText> ComposeMorningBriefAsync(bool ignoreOffHours)
    {
        {
            var now = DateTimeOffset.Now;
            var local = now.LocalDateTime;
            var hours = _services.GetRequiredService<WorkingDaySettings>();
            var greeting = Workspace.Briefing.Greeting is { Length: > 0 } g ? g.TrimEnd('.', '!') : local.Hour < 12 ? "Good morning" : local.Hour < 18 ? "Good afternoon" : "Good evening";

            // Give the slower sources a moment, without holding the briefing up.
            var waits = new List<Task>();
            if (_weather is null && FeatureOn("weather")) waits.Add(RefreshWeatherAsync(force: true));
            if (waits.Count > 0) await Task.WhenAny(Task.WhenAll(waits), Task.Delay(TimeSpan.FromSeconds(12)));

            var breakName = OffHours.IsWorkingDay(local.Date) ? OffHours.BreakName(local.Date) : "";
            var since = new DateTimeOffset(OffHours.PreviousWorkingDayEnd(local, hours));
            var sinceLabel = breakName.Length > 0 ? $"over {breakName}" : "since last night";

            // ---- What's urgent / came in ----
            var managers = TeamsUpdates.Where(u => u.FromManager && !u.Dismissed && !u.Replied && u.At >= since).OrderByDescending(u => u.At).ToList();
            var urgent = VisibleAttentionItems.Where(x => SeverityRank(x.Severity) >= 3).OrderByDescending(x => SeverityRank(x.Severity)).ThenByDescending(x => x.CreatedAt).ToList();
            var people = await SafeAsync(async () => (IReadOnlyList<EmailMessage>)(await _services.GetRequiredService<IEmailService>().GetUnreadInboxAsync(40))
                .Where(m => m.ReceivedAt >= since && !InboxTriageText.IsLikelyNoise(m.SenderAddress, m.SenderName, m.Subject)).ToList(),
                (IReadOnlyList<EmailMessage>)Array.Empty<EmailMessage>());

            // ---- The day ----
            var meetings = CalendarEvents.Where(e => !e.IsAllDay && !e.IsCancelled && e.Attendees.Count > 0 && e.Start.LocalDateTime.Date == local.Date &&
                                                     !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Start).ToList();
            var upcoming = meetings.Where(e => e.End > now).ToList();
            var meetingHours = upcoming.Sum(e => (e.End - e.Start).TotalHours);
            var (gapStart, gapEnd) = LongestClearRun(local, hours, CalendarEvents.Where(e => !e.IsAllDay && e.Start.LocalDateTime.Date == local.Date &&
                                                                                               !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase)).ToList());

            // ---- What's due ----
            var promises = ActiveCommitments.Where(c => IsRealPromise(c) && c.Direction == CommitmentDirection.OwedByMe && c.DueAt is { } d && d.LocalDateTime.Date <= local.Date)
                .OrderBy(c => c.DueAt).ToList();
            var briefing = Workspace.Briefing;
            var duties = FeatureOn("duties") ? Duties.Where(d => d.Enabled).Select(d => (d, due: DutySchedule.NextDue(d, local.Date))).Where(x => x.due <= local.Date).Select(x => x.d).ToList() : [];
            var approvals = Approvals.Where(a => a.Decision.Length == 0).ToList();

            // ---- Out of hours: only what can't wait ----
            if (OffHoursActive && !ignoreOffHours)
            {
                var critical = new List<string>();
                if (managers.Count > 0) critical.Add($"{TeamsSignals.FirstName(managers[0].Sender)} messaged you");
                var offScreen = new StringBuilder($"{greeting.ToUpperInvariant()} · {local:dddd d MMM}\n\n{OffHoursReason(local)}. ");
                offScreen.Append(critical.Count == 0 ? "Nothing urgent." : "Worth knowing: " + string.Join("; ", critical) + ".");
                return new MorningBriefText(offScreen.ToString(),
                    $"{greeting}. I'm off duty{(local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? " for the weekend" : "")}." +
                    (critical.Count == 0 ? " Nothing urgent." : $" Worth knowing: {JoinNames(critical)}.") + " Say I'm working if you need me.", true, breakName);
            }

            // ---- Screen ----
            var screen = new StringBuilder($"MORNING BRIEF · {local:dddd d MMMM}\n");
            void Section(string title, IEnumerable<string> lines)
            {
                var list = lines.Where(l => l.Length > 0).ToList();
                if (list.Count == 0) return;
                screen.Append('\n').AppendLine(title);
                foreach (var l in list) screen.AppendLine($"• {l}");
            }
            var weatherLine = _weather is { } w ? $"{w.Summary}{RainAtHomeTime(w, hours, local)}" : "";
            Section("TODAY", new[] { weatherLine, PlaceLabel.Length > 0 ? $"You're {PlaceLabel.ToLowerInvariant()}." : "" });
            Section($"CAME IN {sinceLabel.ToUpperInvariant()}", managers.Take(3).Select(u => $"{u.Sender} on Teams: {u.Summary}")
                .Concat(urgent.Take(4).Select(x => x.Title))
                .Concat(people.Count > 0 ? [$"{people.Count} email{(people.Count == 1 ? "" : "s")} from people: " + string.Join(", ", people.Select(m => m.SenderName).Distinct().Take(5))] : Array.Empty<string>()));
            Section("YOUR DAY", upcoming.Select(e => $"{e.Start.LocalDateTime:HH:mm} {CleanCalendarObjectiveTitle(e.Subject)}")
                .Append(gapEnd - gapStart >= TimeSpan.FromMinutes(60) ? $"Clear run {gapStart:HH:mm}–{gapEnd:HH:mm} for focus" : ""));
            Section("DUE", promises.Take(4).Select(c => $"Promised{(c.DueAt!.Value.LocalDateTime.Date < local.Date ? " (overdue)" : "")}: {Trim(c.Summary, 80)}")
                .Append(briefing.TasksDueToday + briefing.OverdueTasks > 0 ? $"{briefing.TasksDueToday} task{(briefing.TasksDueToday == 1 ? "" : "s")} due today, {briefing.OverdueTasks} overdue" : "")
                .Concat(duties.Select(d => $"Duty: {d.Name}"))
                .Append(approvals.Count > 0 ? $"{approvals.Count} approval{(approvals.Count == 1 ? "" : "s")} waiting (say \"what needs approving\")" : ""));

            // ---- Where to start ----
            var start = managers.Count > 0 ? $"replying to {TeamsSignals.FirstName(managers[0].Sender)}"
                : promises.Count > 0 ? $"what you promised: {Trim(promises[0].Summary, 60)}"
                : !string.IsNullOrWhiteSpace(briefing.ObjectiveTitle) ? briefing.ObjectiveTitle
                : "";
            if (start.Length > 0) screen.Append('\n').AppendLine($"START WITH: {start}");

            // ---- Spoken: short ----
            var said = new List<string>();
            said.Add(_weather is { } sw ? $"{greeting}. It's {sw.TemperatureC:0} degrees and {sw.Description.ToLowerInvariant()}{RainAtHomeTime(sw, hours, local)}" : $"{greeting}.");
            var headline = new List<string>();
            if (managers.Count > 0) headline.Add($"{TeamsSignals.FirstName(managers[0].Sender)} messaged you");
            if (people.Count > 0) headline.Add($"{people.Count} email{(people.Count == 1 ? "" : "s")} from people");
            said.Add(headline.Count == 0 ? $"Nothing urgent came in {sinceLabel}." : $"{char.ToUpperInvariant(sinceLabel[0])}{sinceLabel[1..]}: {JoinNames(headline.Take(4).ToList())}.");
            said.Add(upcoming.Count == 0 ? "No meetings today" + (gapEnd - gapStart >= TimeSpan.FromHours(2) ? ", so it's a good day for focus." : ".")
                : $"{upcoming.Count} meeting{(upcoming.Count == 1 ? "" : "s")}, about {meetingHours:0.#} hour{(Math.Abs(meetingHours - 1) < 0.05 ? "" : "s")}; the first is {CleanCalendarObjectiveTitle(upcoming[0].Subject)} at {upcoming[0].Start.LocalDateTime:h:mm}" +
                  (gapEnd - gapStart >= TimeSpan.FromMinutes(90) ? $", and you're clear from {gapStart:h:mm} to {gapEnd:h:mm}." : "."));
            var dueBits = new List<string>();
            if (promises.Count > 0) dueBits.Add($"{promises.Count} promise{(promises.Count == 1 ? "" : "s")}");
            if (briefing.OverdueTasks > 0) dueBits.Add($"{briefing.OverdueTasks} overdue task{(briefing.OverdueTasks == 1 ? "" : "s")}");
            if (duties.Count > 0) dueBits.Add(duties.Count == 1 ? duties[0].Name : $"{duties.Count} duties");
            if (approvals.Count > 0) dueBits.Add($"{approvals.Count} approval{(approvals.Count == 1 ? "" : "s")}");
            if (dueBits.Count > 0) said.Add($"Due: {JoinNames(dueBits)}.");
            if (start.Length > 0) said.Add($"I'd start with {start}.");
            return new MorningBriefText(screen.ToString().TrimEnd(), string.Join(" ", said), false, breakName);
        }
    }

    private static string RainAtHomeTime(Nekomata.Core.Weather.WeatherReport w, WorkingDaySettings hours, DateTime local)
    {
        var end = hours.GetEnd(local.Date);
        if (w.Kind is Nekomata.Core.Weather.WeatherKind.Rain or Nekomata.Core.Weather.WeatherKind.Showers or Nekomata.Core.Weather.WeatherKind.Drizzle or Nekomata.Core.Weather.WeatherKind.Thunder)
            return w.RainBetween(end.AddMinutes(-30), end.AddHours(1), 50) is null ? ", drying up by home time" : "";
        return w.RainBetween(end.AddMinutes(-30), end.AddHours(1), 60) is { } r ? $", with rain likely around home time" : "";
    }

    /// <summary>The longest stretch in working hours from now with nothing in the calendar.</summary>
    private static (DateTime Start, DateTime End) LongestClearRun(DateTime now, WorkingDaySettings hours, IReadOnlyList<Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent> events)
    {
        var dayStart = new[] { hours.GetStart(now.Date), now }.Max();
        var dayEnd = hours.GetEnd(now.Date);
        var busy = events.Select(e => (S: e.Start.LocalDateTime, E: e.End.LocalDateTime)).Where(b => b.E > dayStart && b.S < dayEnd).OrderBy(b => b.S).ToList();
        var best = (dayStart, dayStart);
        var cursor = dayStart;
        foreach (var (s, e) in busy)
        {
            if (s > cursor && s - cursor > best.Item2 - best.Item1) best = (cursor, s);
            if (e > cursor) cursor = e;
        }
        if (dayEnd > cursor && dayEnd - cursor > best.Item2 - best.Item1) best = (cursor, dayEnd);
        return best;
    }
}
