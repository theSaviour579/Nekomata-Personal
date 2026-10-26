using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.Neglect;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Integrations;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;

public sealed record HomeSlipButton(string Label, ICommand Act);
public sealed record HomeSlipItem(string Kind, string Title, string Why, Brush Dot, IReadOnlyList<HomeSlipButton> Buttons);
public sealed record HomeMeetingCard(string Key, string MeetingId, string Title, string When, string People, IReadOnlyList<string> Lines, string JoinUrl)
{
    public bool CanJoin => JoinUrl.Length > 0;
}

// The home screen acts on what's slipping and on the next meeting, without asking first.
//
// Slipping: up to four items, each with its own buttons: a promise can be booked, ticked off or marked "not a promise";
// a slipping task can be booked; stalled projects can be put on hold or marked fine. Every button does exactly what the
// spoken command does, so the lessons stick either way.
//
// Next meeting: from 15 minutes before until 5 minutes in, a card with who's coming, what
// the user promised them, and the last email with them. The spoken heads-up arrives 10 minutes out.
public partial class MainViewModel
{
    public ObservableCollection<HomeSlipItem> HomeSlippingItems { get; } = [];
    [ObservableProperty] private HomeMeetingCard? homeMeeting;
    [ObservableProperty] private string homeMeetingCountdown = "";
    private string _homeMeetingDismissedKey = "";
    public bool HasHomeMeeting => HomeMeeting is not null;
    partial void OnHomeMeetingChanged(HomeMeetingCard? value) => OnPropertyChanged(nameof(HasHomeMeeting));
    private bool _homeMeetingBuilding;

    private void RefreshHomeSlipping()
    {
        HomeSlippingItems.Clear();
        foreach (var item in _homeSlipping.Take(4))
        {
            var buttons = new List<HomeSlipButton>();
            var captured = item;
            switch (item.Kind)
            {
                case NeglectKind.UnbookedPromise when item.Promise is { } promise:
                    buttons.Add(new("BOOK", new AsyncRelayCommand(() => HomeSlipRunAsync(() => BookNeglectedWorkAsync([captured])))));
                    buttons.Add(new("DONE", new AsyncRelayCommand(() => HomeSlipRunAsync(() => MarkPromiseKeptAsync(promise)))));
                    buttons.Add(new("NOT A PROMISE", new AsyncRelayCommand(() => HomeSlipRunAsync(() => MarkNotAPromiseAsync(promise)))));
                    break;
                case NeglectKind.SlippingTask or NeglectKind.AvoidedWork when item.TaskId is not null:
                    buttons.Add(new("BOOK", new AsyncRelayCommand(() => HomeSlipRunAsync(() => BookNeglectedWorkAsync([captured])))));
                    break;
                case NeglectKind.StaleProject when item.ProjectIds.Count > 0:
                    buttons.Add(new("ON HOLD", new AsyncRelayCommand(() => HomeSlipRunAsync(() => HoldStalledProjectsAsync(captured)))));
                    buttons.Add(new(item.ProjectIds.Count == 1 ? "IT'S FINE" : "THEY'RE FINE", new AsyncRelayCommand(() =>
                        HomeSlipRunAsync(() => Task.FromResult(MarkProjectsFine(item.ProjectIds.Count == 1 ? item.ProjectNames[0] : ""))))));
                    break;
            }
            var (kind, dot) = item.Kind switch
            {
                NeglectKind.UnbookedPromise => ("PROMISE", WarningBrush),
                NeglectKind.SlippingTask => ("SLIPPING", WarningBrush),
                NeglectKind.AvoidedWork => ("NOT STARTED", PurpleBrush),
                NeglectKind.StaleProject => ("STALLED", MutedBrush),
                _ => ("REPLY OWED", CyanBrush)
            };
            var title = item.Kind == NeglectKind.UnbookedPromise ? PromiseAction(item.Title) : item.Title;
            HomeSlippingItems.Add(new HomeSlipItem(kind, title, item.Why, dot, buttons));
        }
        HomeSlippingText = _homeSlipping.Count == 0 ? "Nothing slipping." : _homeSlipping.Count > 4 ? $"+{_homeSlipping.Count - 4} more · say what's slipping" : "";
    }

    private async Task HomeSlipRunAsync(Func<Task<string>> action)
    {
        string said;
        try { said = await action(); }
        catch (Exception ex) { said = $"That didn't work: {ex.Message}"; }
        _homeSlippingLoaded = DateTimeOffset.MinValue; // re-detect on the next refresh, with the change applied
        RefreshHomeSlipping();
        await RespondAndSpeakAsync(said);
    }

    // The conversation folds away after 15 quiet minutes, so the core is back in view when the user returns to the screen.
    private int _homeChatCount = -1;
    private DateTimeOffset _homeChatChangedAt = DateTimeOffset.Now;

    private void TidyHomeConversation(DateTimeOffset now)
    {
        if (ChatHistory.Count != _homeChatCount)
        {
            _homeChatCount = ChatHistory.Count;
            _homeChatChangedAt = now;
            return;
        }
        if (GuardianPanelExpanded && !_conversationActive && _promiseReview is null && now - _homeChatChangedAt > TimeSpan.FromMinutes(15))
            GuardianPanelExpanded = false;
    }

    // ---------- The next meeting ----------

    private async Task EvaluateHomeMeetingAsync()
    {
        if (_homeMeetingBuilding || !CalendarLoaded) return;
        var now = DateTimeOffset.Now;
        var meeting = CalendarEvents
            .Where(e => !e.IsAllDay && !e.IsNekomataManaged && e.Attendees.Count > 0 && e.End > e.Start &&
                        e.Start - now <= TimeSpan.FromMinutes(15) && now - e.Start <= TimeSpan.FromMinutes(5))
            .OrderBy(e => e.Start)
            .FirstOrDefault();
        if (meeting is null || MeetingPreparationPolicy.Key(meeting) == _homeMeetingDismissedKey)
        {
            HomeMeeting = null;
            return;
        }

        var key = MeetingPreparationPolicy.Key(meeting);
        HomeMeetingCountdown = meeting.Start > now
            ? $"IN {Math.Max(1, (int)Math.Ceiling((meeting.Start - now).TotalMinutes))} MIN · {meeting.Start.LocalDateTime:HH:mm}"
            : $"STARTED {meeting.Start.LocalDateTime:HH:mm}";
        if (HomeMeeting?.Key == key) return;

        _homeMeetingBuilding = true;
        try { HomeMeeting = await BuildHomeMeetingCardAsync(meeting, key); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Meeting card failed safely: {ex.Message}"); }
        finally { _homeMeetingBuilding = false; }
    }

    private sealed record MeetingContext(IReadOnlyList<string> People, IReadOnlyList<string> Promises, string LastEmail)
    {
        public string OneToOneWith { get; init; } = "";
        public IReadOnlyList<string> OneToOne { get; init; } = [];
    }

    private async Task<HomeMeetingCard> BuildHomeMeetingCardAsync(CalendarEvent meeting, string key)
    {
        var context = await GatherMeetingContextAsync(meeting);
        var lines = new List<string>();
        lines.AddRange(context.OneToOne);
        lines.AddRange(context.Promises.Select(p => p.StartsWith("They owe", StringComparison.Ordinal) ? p : $"You promised {p}"));
        if (context.LastEmail.Length > 0) lines.Add(context.LastEmail);
        if (lines.Count == 0) lines.Add("Nothing outstanding with them on record.");
        var title = CleanCalendarObjectiveTitle(meeting.Subject);
        return new HomeMeetingCard(key, meeting.Id, context.OneToOneWith.Length > 0 ? $"1:1 with {context.OneToOneWith} · {title}" : title, "",
            context.People.Count == 0 ? "" : "WITH " + string.Join(", ", context.People).ToUpperInvariant(),
            lines.Take(context.OneToOne.Count > 0 ? 8 : 6).ToList(), meeting.OnlineMeetingUrl ?? "");
    }

    // Who's coming, promises the user made to them, and the most recent email either way.
    private async Task<MeetingContext> GatherMeetingContextAsync(CalendarEvent meeting)
    {
        var email = _services.GetRequiredService<IEmailService>();
        var me = await SafeAsync(() => email.GetMailboxAddressAsync(), "");
        var myDomain = me.Contains('@') ? me.Split('@')[1].ToLowerInvariant() : "";
        var attendees = meeting.Attendees.Where(a => !IsSelf(a, me)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var people = attendees.Select(PersonFirstName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();

        var nameSets = attendees.Where(a => !a.Contains('@')).Select(a => SignificantWords(a)).Where(w => w.Count >= 2).ToList();
        var localSets = attendees.Where(a => a.Contains('@')).Select(a => a.Split('@')[0].Split('.', '_', '-').Where(s => s.Length > 1).Select(s => s.ToLowerInvariant()).ToHashSet())
            .Where(w => w.Count >= 2).ToList();
        var firstNames = people.Select(p => p.ToLowerInvariant()).ToHashSet();
        var addresses = attendees.Where(a => a.Contains('@')).Select(a => a.ToLowerInvariant()).ToHashSet();
        var promises = ActiveCommitments
            .Where(IsRealPromise)
            .Where(c => !IsTaughtNotAPromise(c.Summary))
            .Where(c => addresses.Contains(c.Counterparty.Trim().ToLowerInvariant()) ||
                        (!IsSelf(c.Counterparty, me) && firstNames.Contains(PersonFirstName(c.Counterparty).ToLowerInvariant())))
            .Take(3)
            .Select(c => $"{PersonFirstName(c.Counterparty)}: {PromiseSpoken(c.Summary)}{(c.DueAt is { } d && d < DateTimeOffset.Now ? " (overdue)" : "")}")
            .ToList();

        var lastEmail = "";
        if (addresses.Count > 0 || nameSets.Count > 0)
        {
            var inbox = await SafeAsync<IReadOnlyList<EmailMessage>>(() => email.GetRecentInboxAsync(100), Array.Empty<EmailMessage>());
            var sent = await SafeAsync<IReadOnlyList<EmailMessage>>(() => email.GetRecentSentMessagesAsync(60), Array.Empty<EmailMessage>());
            bool Involves(string address, string name) =>
                addresses.Contains(address.ToLowerInvariant()) || nameSets.Any(set => set.All(SignificantWords(name).Contains));
            var from = inbox.Where(m => Involves(m.SenderAddress, m.SenderName)).Select(m => (m.ReceivedAt, Text: $"{PersonFirstName(m.SenderName)} emailed"));
            var to = sent.Where(m => m.ToRecipientAddresses.Any(a => addresses.Contains(a.ToLowerInvariant())) || m.ToRecipients.Any(n => Involves("", n)))
                .Select(m => (m.ReceivedAt, Text: "You emailed them"));
            var latest = from.Concat(to).OrderByDescending(x => x.ReceivedAt).FirstOrDefault();
            if (latest.Text is not null)
            {
                var subject = inbox.Concat(sent).First(m => m.ReceivedAt == latest.ReceivedAt).Subject;
                lastEmail = $"Last email: {latest.Text} {latest.ReceivedAt.LocalDateTime:ddd HH:mm}, \"{Trim(subject, 50)}\"";
            }
        }
        var context = new MeetingContext(people, promises, lastEmail);
        if (attendees.Count == 1) context = await AddOneToOneAsync(context, meeting, attendees[0], me);
        return context;
    }

    // ---------- 1:1s with the team ----------

    private sealed class OneToOneDates { public Dictionary<string, string> Last { get; set; } = []; public Dictionary<string, string> Previous { get; set; } = []; }
    private static string OneToOnePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "one-to-ones.json");

    // A meeting with exactly one other person who is on the team (a Team Skills profile): when your last 1:1 with them
    // was, and what you owe each other.
    private async Task<MeetingContext> AddOneToOneAsync(MeetingContext context, CalendarEvent meeting, string attendee, string me)
    {
        var profiles = await SafeAsync(() => _services.GetRequiredService<Nekomata.Data.Repositories.ITeamProfileRepository>().GetAllAsync(), []);
        var attendeeWords = attendee.Contains('@')
            ? attendee.Split('@')[0].Split('.', '_', '-').Where(s => s.Length > 1).Select(s => s.ToLowerInvariant()).ToHashSet()
            : SignificantWords(attendee);
        bool Matches(string name) { var w = SignificantWords(name); return w.Count >= 2 && w.All(attendeeWords.Contains) || attendeeWords.Count >= 2 && attendeeWords.All(w.Contains); }
        var profile = profiles.FirstOrDefault(p => p.Active && Matches(p.Name));
        if (profile is null) return context;

        var name = PersonFirstName(profile.Name);
        var lines = new List<string>();

        // The date of this meeting is remembered for next time.
        var dates = LoadJson<OneToOneDates>(OneToOnePath) ?? new OneToOneDates();
        var key = profile.Name.ToLowerInvariant();
        var thisDate = meeting.Start.LocalDateTime.Date.ToString("yyyy-MM-dd");
        if (dates.Last.TryGetValue(key, out var last) && last != thisDate) dates.Previous[key] = last;
        dates.Last[key] = thisDate;
        SaveJson(OneToOnePath, dates);
        if (dates.Previous.TryGetValue(key, out var previous) && DateTime.TryParse(previous, out var since))
            lines.Add($"Last 1:1 with {name}: {since:ddd d MMM}");

        // What they owe the user (their own firm promises).
        var theyOwe = ActiveCommitments.Where(IsRealExpectation)
            .Where(c => Matches(c.Owner) || PersonFirstName(c.Owner).Equals(name, StringComparison.OrdinalIgnoreCase))
            .Take(2).Select(c => $"They owe you: {PromiseSpoken(c.Summary)}").ToList();

        return context with
        {
            OneToOneWith = name,
            OneToOne = lines,
            Promises = context.Promises.Concat(theyOwe).ToList()
        };
    }

    [RelayCommand]
    private void JoinHomeMeeting()
    {
        if (HomeMeeting is not { CanJoin: true } card) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.JoinUrl) { UseShellExecute = true }); }
        catch (Exception ex) { ShowPaletteResponse($"Couldn't open the meeting link: {ex.Message}"); }
    }

    [RelayCommand]
    private void OpenHomeMeetingBrief()
    {
        if (HomeMeeting is not { } card) return;
        var item = AttentionItems.FirstOrDefault(a => a.Key == card.Key);
        if (item is not null) ShowMeetingPreparation(item);
        else ShowPaletteResponse("The full brief is still being put together. Try again in a moment.");
    }

    [RelayCommand]
    private void DismissHomeMeeting()
    {
        if (HomeMeeting is not { } card) return;
        _homeMeetingDismissedKey = card.Key;
        HomeMeeting = null;
    }
}
