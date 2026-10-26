namespace Nekomata.UI.ViewModels;

// A typed prompt after each meeting (the user works in an open office, so no speaking). A few minutes after a meeting with
// other people ends, the quick-note box appears at the top of the screen without taking focus: "ANYTHING FROM THE OPS
// REVIEW?". Clicking into it and typing files the note against that meeting (as "note for meeting" does), ready for the
// usual Capture outcomes review; ignored, it closes itself. Not during a call or presentation, off duty, or when away.
public partial class MainViewModel
{
    private static readonly TimeSpan MeetingPromptWindow = TimeSpan.FromMinutes(5);
    private readonly HashSet<string> _meetingPrompted = [];

    private void MaybePromptAfterMeeting(DateTimeOffset now)
    {
        if (!FeatureOn("meeting-notes") || IsInitialLoading || OffHoursActive || _awaySince is not null ||
            LiveCallReason() is not null || _teamsBusyReason == "presenting") return;
        var ended = CalendarEvents
            .Where(e => !e.IsAllDay && !e.IsNekomataManaged && !e.IsCancelled && e.Attendees.Count > 0 &&
                        !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase) &&
                        e.End <= now && now - e.End <= MeetingPromptWindow && e.End - e.Start >= TimeSpan.FromMinutes(10))
            .OrderByDescending(e => e.End)
            .FirstOrDefault();
        if (ended is null || !_meetingPrompted.Add($"{ended.Id}:{ended.Start.UtcDateTime:O}")) return;
        var subject = CleanCalendarObjectiveTitle(ended.Subject);
        if (subject.Length > 40) subject = subject[..40].TrimEnd() + "…";
        new Nekomata.UI.Windows.QuickNoteWindow(NoteForMeetingAsync,
            $"ANYTHING FROM {subject.ToUpperInvariant()}? · click to type · Enter to save · Esc to skip", passive: true).Show();
        Think($"Asked for notes from {subject}");
    }
}
