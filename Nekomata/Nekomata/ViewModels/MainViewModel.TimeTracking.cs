using System.IO;
using System.Text;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Planning;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Planning;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Where the time goes. Every 15 seconds Guardian notes the category of what the user is doing (a meeting in the calendar or a
// call wins, then a focus mission, then the app in front: Outlook, Teams, Word, admin portals...). Only the
// category and minutes are kept (time-ledger.json, 120 days): never window titles or content. "Where did my time go?"
// shows today; "time this week" the week, with reactive (email, chat, calls) vs planned (focus, projects,
// documents) and the change from last week. Friday afternoon it says the week's split once.
//
// The same ledger drives gentle working-pattern nudges (worked late three days, no lunch break for three days, ten-hour
// days, no focus block for a week): at most one of each kind a week, in Needs you, never spoken.
//
// And meeting load: on Friday afternoon next week's meeting hours against the budget on the settings page, with the
// best candidates to drop; on the first Monday of the month a review of recurring meetings ("you declined 4 of the last
// 6 Ops stand-ups: drop it?"), with polite messages drafted in Outlook on "do it" (never sent, never moved).
public partial class MainViewModel
{
    private static string TimeLedgerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "time-ledger.json");
    private static readonly TimeSpan TimeSample = TimeSpan.FromSeconds(15);
    private readonly DispatcherTimer _timeTimer = new() { Interval = TimeSample };
    private List<TimeEntry>? _timeLedger;
    private DateTimeOffset _timeSavedAt;
    private DateTime _timeWeekNoteOn, _wellbeingCheckedOn, _meetingLoadWeek, _meetingReviewMonth;
    private DateTime? _lastFocusSeen;

    private List<TimeEntry> TimeLedgerDays => _timeLedger ??= LoadJson<List<TimeEntry>>(TimeLedgerPath) ?? [];

    private void InitialiseTimeTracking()
    {
        _timeTimer.Tick += (_, _) => SampleTime();
        _timeTimer.Start();
    }

    private TimeEntry TodayLedger()
    {
        var date = DateTime.Today;
        var day = TimeLedgerDays.FirstOrDefault(d => d.Date == date);
        if (day is null)
        {
            day = new TimeEntry { Date = date };
            TimeLedgerDays.Add(day);
            TimeLedgerDays.RemoveAll(d => d.Date < date.AddDays(-120));
        }
        return day;
    }

    private void SampleTime()
    {
        if (!FeatureOn("time")) return;
        try
        {
            var now = DateTime.Now;
            var idle = ScreenContextService.IdleTime();
            var day = TodayLedger();
            TimeLedger.NoteInput(day, now - idle);
            var hwnd = ScreenContextService.Foreground();
            var (app, title) = ScreenContextService.Describe(hwnd);
            var nowOffset = DateTimeOffset.Now;
            var inMeeting = CalendarEvents.Any(e => !e.IsAllDay && e.Attendees.Count > 0 && e.Start <= nowOffset && e.End > nowOffset &&
                                                    !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase));
            if (MissionActive) _lastFocusSeen = now;
            var category = TimeLedger.Categorise(app, title, inMeeting, IsOnPhoneCall, MissionActive, idle >= TimeSpan.FromMinutes(5));
            // A locked or sleeping machine (or a long idle) isn't counted at all.
            if (idle >= TimeSpan.FromMinutes(30)) return;
            TimeLedger.Add(day, now, TimeSample.TotalMinutes, category);
            if (nowOffset - _timeSavedAt >= TimeSpan.FromMinutes(5))
            {
                _timeSavedAt = nowOffset;
                SaveJson(TimeLedgerPath, TimeLedgerDays);
                _plannerViewModel?.RefreshTime();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Time sample skipped: {ex.Message}"); }
    }

    private static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    private string TimeReport(DateTime from, DateTime to, string heading)
    {
        var days = TimeLedgerDays.Where(d => d.Date >= from && d.Date <= to).ToList();
        var sb = new StringBuilder($"{heading}\n\n");
        sb.AppendLine(TimeLedger.Summary(days));
        var total = days.Sum(TimeLedger.Total);
        if (total >= 1)
        {
            sb.AppendLine().AppendLine($"Total {TimeLedger.Hours(total)} · reactive {TimeLedger.ReactivePercent(days)}% of reactive + planned work");
            var first = days.Where(d => d.FirstActive is not null).Select(d => d.FirstActive!.Value.TimeOfDay).DefaultIfEmpty().Min();
            var last = days.Where(d => d.LastActive is not null).Select(d => d.LastActive!.Value.TimeOfDay).DefaultIfEmpty().Max();
            if (days.Count == 1 && days[0].FirstActive is { } f) sb.AppendLine($"First active {f:HH:mm}, last {days[0].LastActive:HH:mm}.");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task WhereDidMyTimeGoAsync(bool week)
    {
        var today = DateTime.Today;
        if (!week)
        {
            var day = TodayLedger();
            ShowPaletteResponse(TimeReport(today, today, $"TODAY · {today:dddd d MMM}"));
            var top = day.Minutes.Where(m => m.Key != TimeCategory.Away).OrderByDescending(m => m.Value).Take(2).ToList();
            await SpeakGuardianAsync(top.Count == 0 ? "I haven't recorded anything today yet." :
                $"Mostly {TimeLedger.Label(top[0].Key).ToLowerInvariant()}, {TimeLedger.Hours(top[0].Value)}{(top.Count > 1 ? $", then {TimeLedger.Label(top[1].Key).ToLowerInvariant()}" : "")}. " +
                $"{TimeLedger.ReactivePercent([day])}% reactive.", true);
            return;
        }
        var start = WeekStart(today);
        var thisWeek = TimeLedgerDays.Where(d => d.Date >= start).ToList();
        var lastWeek = TimeLedgerDays.Where(d => d.Date >= start.AddDays(-7) && d.Date < start).ToList();
        var text = TimeReport(start, today, $"THIS WEEK · from {start:d MMM}");
        if (lastWeek.Count > 0) text += $"\nLast week: {TimeLedger.ReactivePercent(lastWeek)}% reactive, {TimeLedger.Hours(lastWeek.Sum(TimeLedger.Total))} in total.";
        ShowPaletteResponse(text);
        await SpeakGuardianAsync($"This week has been {TimeLedger.ReactivePercent(thisWeek)}% reactive" +
                                 (lastWeek.Count > 0 ? $", against {TimeLedger.ReactivePercent(lastWeek)}% last week." : "."), true);
    }

    // Friday from 15:30: the week in one line. Daily from 10:00: working-pattern nudges.
    private void MaybeTimeNotes(DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var start = WeekStart(local);
        if (local.DayOfWeek == DayOfWeek.Friday && local.TimeOfDay >= new TimeSpan(15, 30, 0) && _timeWeekNoteOn != start && FeatureOn("time"))
        {
            _timeWeekNoteOn = start;
            var thisWeek = TimeLedgerDays.Where(d => d.Date >= start).ToList();
            var lastWeek = TimeLedgerDays.Where(d => d.Date >= start.AddDays(-7) && d.Date < start).ToList();
            if (thisWeek.Sum(TimeLedger.Total) >= 300)
            {
                var top = thisWeek.SelectMany(d => d.Minutes).Where(m => m.Key != TimeCategory.Away).GroupBy(m => m.Key)
                    .Select(g => (g.Key, Minutes: g.Sum(x => x.Value))).OrderByDescending(x => x.Minutes).Take(3).ToList();
                RaiseAttention($"time-week:{start:yyyyMMdd}", "Time", "Info",
                    $"This week: {TimeLedger.ReactivePercent(thisWeek)}% reactive" + (lastWeek.Count > 0 ? $" (last week {TimeLedger.ReactivePercent(lastWeek)}%)" : ""),
                    "Most time: " + string.Join(", ", top.Select(t => $"{TimeLedger.Label(t.Key).ToLowerInvariant()} {TimeLedger.Hours(t.Minutes)}")) + ". Say \"time this week\" for the breakdown.",
                    "time_week");
            }
        }
        if (_wellbeingCheckedOn != local.Date && local.TimeOfDay >= new TimeSpan(10, 0, 0) && FeatureOn("wellbeing") && FeatureOn("time"))
        {
            _wellbeingCheckedOn = local.Date;
            var hours = _services.GetRequiredService<WorkingDaySettings>();
            var recent = TimeLedgerDays.Where(d => d.Date >= local.Date.AddDays(-10) && OffHours.IsWorkingDay(d.Date)).OrderBy(d => d.Date).ToList();
            foreach (var nudge in WorkPattern.Nudges(recent, hours.EndTime, local.Date, _lastFocusSeen ?? recent.Where(d => d.Minutes.GetValueOrDefault(TimeCategory.Focus) >= 30).Select(d => (DateTime?)d.Date).LastOrDefault()))
                RaiseAttention($"wellbeing:{nudge.Key}:{start:yyyyMMdd}", "Guardian", "Info", nudge.Title, nudge.Detail, "none");
        }
    }

    // ---- Meeting load ----

    private static MeetingInstance Instance(CalendarEvent e) =>
        new(e.Subject, e.Organiser, e.Start, e.End, e.IsOrganizer, e.ResponseStatus, e.Attendees.Count, e.IsCancelled);

    private async Task MaybeMeetingLoadAsync(DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var start = WeekStart(local);
        if (local.DayOfWeek == DayOfWeek.Friday && local.TimeOfDay >= new TimeSpan(14, 0, 0) && _meetingLoadWeek != start)
        {
            _meetingLoadWeek = start;
            var budget = Prefs.MeetingBudgetHours;
            if (budget > 0)
            {
                var next = start.AddDays(7);
                var events = await SafeAsync(() => _services.GetRequiredService<ICalendarService>().GetEventsAsync(new DateTimeOffset(next), new DateTimeOffset(next.AddDays(5))),
                    (IReadOnlyList<CalendarEvent>)Array.Empty<CalendarEvent>());
                var meetings = events.Where(e => !e.IsAllDay && !e.IsNekomataManaged).Select(Instance).ToList();
                var hours = MeetingLoad.Hours(meetings);
                if (hours > budget)
                {
                    var candidates = events.Where(e => !e.IsAllDay && !e.IsOrganizer && e.Attendees.Count > 0 && !e.IsCancelled &&
                                                       !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(e => e.Attendees.Count >= 8).ThenByDescending(e => (e.End - e.Start).TotalMinutes).Take(3)
                        .Select(e => $"{e.Start.LocalDateTime:ddd HH:mm} {CleanCalendarObjectiveTitle(e.Subject)} ({(int)(e.End - e.Start).TotalMinutes}m, {e.Attendees.Count} people)").ToList();
                    RaiseAttention($"meeting-load:{next:yyyyMMdd}", "Calendar", "Info", $"Next week has {hours:0.#} hours of meetings (budget {budget})",
                        (candidates.Count > 0 ? "Biggest ones you don't run: " + string.Join("; ", candidates) + ". " : "") + "Worth declining or sending someone?", "none");
                }
            }
        }
        var firstMonday = new DateTime(local.Year, local.Month, 1);
        while (firstMonday.DayOfWeek != DayOfWeek.Monday) firstMonday = firstMonday.AddDays(1);
        if (local.Date == firstMonday && local.TimeOfDay >= new TimeSpan(10, 0, 0) && _meetingReviewMonth != firstMonday)
        {
            _meetingReviewMonth = firstMonday;
            var review = await RecurringReviewAsync();
            var changes = review.Where(r => r.Suggestion != "Keep").ToList();
            if (changes.Count > 0)
                RaiseAttention($"meeting-review:{local:yyyyMM}", "Calendar", "Info", $"Recurring meetings: {changes.Count} worth changing",
                    string.Join("; ", changes.Take(3).Select(c => $"{c.Subject}: {c.Suggestion.ToLowerInvariant()} ({c.Why})")) + ". Open for the review.", "meeting_review");
        }
    }

    private async Task<IReadOnlyList<RecurringMeeting>> RecurringReviewAsync()
    {
        var end = DateTimeOffset.Now;
        var events = await SafeAsync(() => _services.GetRequiredService<ICalendarService>().GetEventsAsync(end.AddDays(-56), end),
            (IReadOnlyList<CalendarEvent>)Array.Empty<CalendarEvent>());
        return MeetingLoad.Recurring(events.Where(e => !e.IsAllDay && !e.IsNekomataManaged).Select(Instance));
    }

    private async Task MeetingReviewAsync()
    {
        Think("Looking back over eight weeks of meetings");
        var review = await RecurringReviewAsync();
        if (review.Count == 0) { await RespondAndSpeakAsync("I couldn't find any recurring meetings in the last eight weeks."); return; }
        var sb = new StringBuilder("RECURRING MEETINGS · last 8 weeks\n");
        foreach (var r in review)
            sb.AppendLine().AppendLine($"{r.Suggestion.ToUpperInvariant()} · {r.Subject}")
              .AppendLine($"  {r.Occurrences}× · {r.AverageMinutes} min · {r.Attendees} people · ~{r.HoursPerMonth}h a month{(r.YouOrganise ? " · you organise" : $" · {r.Organiser}")}{(r.Why.Length > 0 ? $" · {r.Why}" : "")}");
        var changes = review.Where(r => r.Suggestion != "Keep").ToList();
        if (changes.Count > 0) sb.AppendLine().Append("Say do it and I'll draft a short message for each change in Outlook. Nothing is sent or moved.");
        ShowCopyWindow("Meeting review", "RECURRING MEETING REVIEW", sb.ToString().TrimEnd());
        if (changes.Count > 0) SetPendingAutonomyAction($"draft {changes.Count} meeting message{(changes.Count == 1 ? "" : "s")} in Outlook", () => DraftMeetingMessagesAsync(changes));
        var hours = changes.Sum(c => c.HoursPerMonth);
        await SpeakGuardianAsync(changes.Count == 0 ? "Your recurring meetings all look worth keeping." :
            $"{changes.Count} recurring meeting{(changes.Count == 1 ? "" : "s")} worth changing, about {hours:0} hours a month. Say do it for draft messages.", true);
    }

    private async Task DraftMeetingMessagesAsync(IReadOnlyList<RecurringMeeting> changes)
    {
        var email = _services.GetRequiredService<IEmailService>();
        var drafted = 0;
        foreach (var c in changes)
        {
            var (subject, body) = c.Suggestion switch
            {
                "Drop it" or "Drop it or reply" => ($"{c.Subject}", $"Hi,\n\nI've not been able to make most of the recent {c.Subject} sessions, so I'm going to step out of the series for now. " +
                                                                     $"Happy to be sent the notes, or pulled in when something needs me.\n\nThanks,\n{UserFirstName}"),
                "Ask to shorten" => ($"{c.Subject}: shorter?", $"Hi,\n\nWould it work to run {c.Subject} at 30 or 45 minutes instead of {c.AverageMinutes}? " +
                                                               $"Happy to try it for a few weeks and see.\n\nThanks,\n{UserFirstName}"),
                "Shorten to 45" => ($"{c.Subject}: moving to 45 minutes", $"Hi all,\n\nFrom the next one I'd like to run {c.Subject} at 45 minutes. Same agenda, a bit tighter.\n\nThanks,\n{UserFirstName}"),
                "Make it fortnightly" => ($"{c.Subject}: going fortnightly", $"Hi all,\n\nI'm thinking of moving {c.Subject} to fortnightly. Shout if that causes a problem; " +
                                                                             $"anything urgent in between, just message me.\n\nThanks,\n{UserFirstName}"),
                _ => ("", "")
            };
            if (subject.Length == 0) continue;
            var to = c.YouOrganise ? Array.Empty<string>() : c.Organiser.Contains('@') ? [c.Organiser] : Array.Empty<string>();
            try { await email.CreateNewDraftAsync(to, subject, body); drafted++; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Meeting draft failed: {ex.Message}"); }
        }
        RecordDone($"Drafted {drafted} meeting message{(drafted == 1 ? "" : "s")} in Outlook.");
        await RespondAndSpeakAsync($"{drafted} draft{(drafted == 1 ? " is" : "s are")} in Outlook. Check the recipients and send the ones you want; nothing in your calendar has changed.");
    }

    private async Task MeetingLoadNowAsync()
    {
        var start = WeekStart(DateTime.Today);
        var next = start.AddDays(7);
        var calendar = _services.GetRequiredService<ICalendarService>();
        var thisWeek = await SafeAsync(() => calendar.GetEventsAsync(new DateTimeOffset(start), new DateTimeOffset(start.AddDays(5))), (IReadOnlyList<CalendarEvent>)Array.Empty<CalendarEvent>());
        var nextWeek = await SafeAsync(() => calendar.GetEventsAsync(new DateTimeOffset(next), new DateTimeOffset(next.AddDays(5))), (IReadOnlyList<CalendarEvent>)Array.Empty<CalendarEvent>());
        double H(IReadOnlyList<CalendarEvent> e) => MeetingLoad.Hours(e.Where(x => !x.IsAllDay && !x.IsNekomataManaged).Select(Instance));
        var budget = Prefs.MeetingBudgetHours;
        await RespondAndSpeakAsync($"This week has {H(thisWeek):0.#} hours of meetings; next week {H(nextWeek):0.#}" +
                                   (budget > 0 ? $", against your budget of {budget}." : ".") + " Say \"review my recurring meetings\" for what to change.");
    }
}
