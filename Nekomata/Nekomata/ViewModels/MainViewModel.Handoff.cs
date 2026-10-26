using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Integrations;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public sealed record HandoffSection(string Label, IReadOnlyList<string> Lines);

// The evening hand-off. Shortly after the working day ends (once a day), or on "hand off" / "what does tomorrow look
// like": one card on Home and a short spoken version.
//   DONE: tasks finished and what Guardian did for you.
//   MOVED: what rolled to tomorrow and why (the wrap-up decisions, or focus blocks that passed without being finished).
//   TOMORROW: clashes (overlaps, a meeting on a focus block, three hours of back-to-back meetings), promises due, hours in meetings.
//   FIRST UP: the first thing tomorrow, with what you need for it (who's in the meeting).
public partial class MainViewModel
{
    public ObservableCollection<HandoffSection> HomeHandoff { get; } = [];
    [ObservableProperty] private bool homeShowHandoff;
    [ObservableProperty] private string homeHandoffTitle = "";

    private static string HandoffDonePath(DateTime day) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", $"handoff-{day:yyyy-MM-dd}.done");

    [RelayCommand] private void HideHomeHandoff() => HomeShowHandoff = false;

    [RelayCommand]
    private Task ShowHandoffAsync() => BuildHandoffAsync(speak: true);

    /// <summary>Called on each home refresh: the automatic once-a-day hand-off, five minutes after the day ends.</summary>
    private async Task MaybeRunHandoffAsync(DateTimeOffset now, WorkingDaySettings settings)
    {
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
        var end = settings.GetEnd(now.Date);
        if (now.LocalDateTime < end.AddMinutes(5) || now.LocalDateTime > end.AddHours(5) || File.Exists(HandoffDonePath(now.Date))) return;
        if (_guardianSpeakingDepth > 0 || _conversationActive || DayReviewVisible) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(HandoffDonePath(now.Date))!); File.WriteAllText(HandoffDonePath(now.Date), now.ToString("O")); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Hand-off marker not saved: {ex.Message}"); }
        await BuildHandoffAsync(speak: true);
    }

    private async Task BuildHandoffAsync(bool speak)
    {
        var now = DateTimeOffset.Now;
        var today = now.LocalDateTime.Date;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var nextDay = Nekomata.Core.Guardian.Outcomes.WrapUpPlanning.NextWorkday(today);
        var dayName = nextDay == today.AddDays(1) ? "tomorrow" : nextDay.ToString("dddd");

        // DONE
        var done = new List<string>();
        var finished = Workspace.Tasks.Where(t => t.CompletedAt?.Date == today).Select(t => t.Title).Distinct().ToList();
        if (finished.Count > 0) done.Add($"{finished.Count} task{(finished.Count == 1 ? "" : "s")} finished: {string.Join(", ", finished.Take(4))}{(finished.Count > 4 ? "…" : "")}");
        var guardian = _doneToday.Count(d => d.At.Date == today);
        if (guardian > 0) done.Add($"Guardian did {guardian} thing{(guardian == 1 ? "" : "s")} for you");
        if (done.Count == 0) done.Add("Nothing recorded as finished today.");

        // MOVED
        var moved = new List<string>();
        foreach (var o in WrapUpObjectives.Where(o => o.Decision is "Continue next workday" or "Defer until"))
            moved.Add($"{o.Title}: {(o.Decision == "Defer until" ? "deferred" : $"continues {dayName}")}");
        if (moved.Count == 0)
        {
            var unfinished = _autopilotToday.Where(e => e.IsNekomataManaged && e.End <= now && e.Start.LocalDateTime.Date == today)
                .Where(e => ResolveCalendarTaskId(e) is not long id || Workspace.Tasks.Any(t => t.Id == id && !t.IsCompleted && !t.Completed))
                .Select(e => CleanCalendarObjectiveTitle(e.Subject)).Distinct().Take(5).ToList();
            moved.AddRange(unfinished.Select(t => $"{t}: its block passed without being finished"));
        }
        if (moved.Count > 0 && DayReview.InterruptionMinutes >= 30)
            moved.Add($"Why: {DayReview.InterruptionMinutes / 60}h {DayReview.InterruptionMinutes % 60:00}m went to {DayReview.InterruptionCount} interruption{(DayReview.InterruptionCount == 1 ? "" : "s")} today");
        if (moved.Count == 0) moved.Add("Nothing rolls forward.");

        // TOMORROW
        var events = _homeTomorrow.Count > 0 && _homeTomorrow.All(e => e.Start.LocalDateTime.Date == nextDay) ? _homeTomorrow
            : await SafeAsync<IReadOnlyList<CalendarEvent>>(() => _services.GetRequiredService<Nekomata.Integrations.MicrosoftGraph.Calendar.ICalendarService>()
                .GetEventsAsync(new DateTimeOffset(nextDay, TimeZoneInfo.Local.GetUtcOffset(nextDay)), new DateTimeOffset(nextDay.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(nextDay))), Array.Empty<CalendarEvent>());
        var timed = events.Where(e => !e.IsAllDay && e.End > e.Start).OrderBy(e => e.Start).ToList();
        var tomorrow = HandoffClashes(timed, settings, nextDay);
        var meetingMinutes = timed.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0).Sum(e => (e.End - e.Start).TotalMinutes);
        if (meetingMinutes > 0) tomorrow.Add($"{meetingMinutes / 60:0.#} hours in meetings");
        var promises = ActiveCommitments.Where(IsRealPromise).Where(c => !IsTaughtNotAPromise(c.Summary) && c.DueAt?.LocalDateTime.Date == nextDay).Take(3)
            .Select(c => $"Promise due: {PromiseAction(c.Summary)}{(IsSelf(c.Counterparty, "") ? "" : $" for {PersonFirstName(c.Counterparty)}")}");
        tomorrow.AddRange(promises);
        if (tomorrow.Count == 0) tomorrow.Add("No clashes. Nothing due.");

        // FIRST UP
        var first = timed.FirstOrDefault();
        var firstUp = new List<string>();
        if (first is null) firstUp.Add($"Nothing booked yet. Say plan tomorrow and Guardian will lay out {dayName}.");
        else
        {
            firstUp.Add($"{first.Start.LocalDateTime:HH:mm} {CleanCalendarObjectiveTitle(first.Subject)}");
            if (!first.IsNekomataManaged && first.Attendees.Count > 0)
                firstUp.Add($"With {SpokenNameList(first.Attendees)}");
        }

        HomeHandoff.Clear();
        HomeHandoff.Add(new HandoffSection("DONE TODAY", done));
        HomeHandoff.Add(new HandoffSection($"MOVED TO {dayName.ToUpperInvariant()}", moved));
        HomeHandoff.Add(new HandoffSection(dayName.ToUpperInvariant(), tomorrow));
        HomeHandoff.Add(new HandoffSection("FIRST UP", firstUp));
        HomeHandoffTitle = $"HAND-OFF · {today:dddd d MMM}";
        HomeShowHandoff = true;

        if (!speak) return;
        var movedCount = moved.Count(m => !m.StartsWith("Why:", StringComparison.Ordinal) && m != "Nothing rolls forward.");
        var clashCount = tomorrow.Count(l => l.StartsWith("Clash", StringComparison.Ordinal) || l.StartsWith("Back-to-back", StringComparison.Ordinal) || l.StartsWith("Early", StringComparison.Ordinal));
        var spoken = $"Here's your hand-off. {(finished.Count > 0 ? $"{finished.Count} thing{(finished.Count == 1 ? "" : "s")} done today. " : "")}" +
                     (movedCount == 0 ? "Nothing rolls forward. " : $"{movedCount} thing{(movedCount == 1 ? "" : "s")} move to {dayName}. ") +
                     (first is null ? $"Nothing is booked for {dayName} yet. " : $"{char.ToUpperInvariant(dayName[0])}{dayName[1..]} starts at {first.Start.LocalDateTime:HH:mm} with {SpokenTitle(CleanCalendarObjectiveTitle(first.Subject))}. ") +
                     (clashCount == 0 ? "No clashes." : $"Watch for {clashCount} clash{(clashCount == 1 ? "" : "es")}; it's on screen.");
        await SpeakGuardianAsync(spoken);
    }

    // Overlaps, a meeting landing on a focus block, three hours or more of meetings with no gap, and anything before
    // the working day starts.
    private static List<string> HandoffClashes(IReadOnlyList<CalendarEvent> timed, WorkingDaySettings settings, DateTime day)
    {
        var lines = new List<string>();
        for (var i = 0; i < timed.Count; i++)
            for (var j = i + 1; j < timed.Count && timed[j].Start < timed[i].End; j++)
            {
                var a = timed[i]; var b = timed[j];
                var focus = a.IsNekomataManaged ? a : b.IsNekomataManaged ? b : null;
                var meeting = focus == a ? b : a;
                lines.Add(focus is not null && !meeting.IsNekomataManaged
                    ? $"Clash at {b.Start.LocalDateTime:HH:mm}: {CleanCalendarObjectiveTitle(meeting.Subject)} lands on your {CleanCalendarObjectiveTitle(focus.Subject)} block (Guardian will move the block)"
                    : $"Clash at {b.Start.LocalDateTime:HH:mm}: {CleanCalendarObjectiveTitle(a.Subject)} and {CleanCalendarObjectiveTitle(b.Subject)} overlap");
            }
        var meetings = timed.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0).ToList();
        var runStart = DateTimeOffset.MinValue; var runEnd = DateTimeOffset.MinValue;
        foreach (var m in meetings)
        {
            if (m.Start > runEnd.AddMinutes(5)) { runStart = m.Start; runEnd = m.End; }
            else if (m.End > runEnd) runEnd = m.End;
            if (runEnd - runStart >= TimeSpan.FromHours(3) && !lines.Any(l => l.StartsWith("Back-to-back", StringComparison.Ordinal)))
                lines.Add($"Back-to-back meetings {runStart.LocalDateTime:HH:mm}–{runEnd.LocalDateTime:HH:mm} with no break");
        }
        var start = settings.GetStart(day);
        foreach (var early in meetings.Where(m => m.Start.LocalDateTime < start).Take(1))
            lines.Add($"Early start: {CleanCalendarObjectiveTitle(early.Subject)} at {early.Start.LocalDateTime:HH:mm}, before your day begins");
        return lines.Take(4).ToList();
    }
}
