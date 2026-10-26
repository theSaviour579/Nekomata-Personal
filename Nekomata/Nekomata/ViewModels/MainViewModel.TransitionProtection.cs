using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private void EvaluateTransitionProtection(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        if (!MissionActive || Workspace.CurrentMission is null) return;
        var mission = Workspace.CurrentMission;
        var activeCommitment = events.Where(x => !x.IsAllDay && x.Start <= now && x.End > now)
            .FirstOrDefault(x => !IsWorkingOnCalendarEvent(x));
        if (activeCommitment is not null)
        {
            var overrun = Math.Max(0, (int)Math.Floor((now - activeCommitment.Start).TotalMinutes));
            EmitTransitionAttention(activeCommitment, "conflict", "BLOCK CONFLICT DETECTED", "High",
                $"{mission.Title} has overrun into {CleanCalendarObjectiveTitle(activeCommitment.Subject)} by {overrun} minute{(overrun == 1 ? "" : "s")}. Guardian recommends ending the current session and preserving the scheduled commitment.", now, speak: true);
            return;
        }

        var next = events.Where(x => !x.IsAllDay && x.Start > now).OrderBy(x => x.Start).FirstOrDefault();
        if (next is null) return;
        var minutesUntil = (next.Start - now).TotalMinutes;
        if (minutesUntil > 15) return;
        var remaining = Math.Max(0, (int)Math.Ceiling((MissionSessionEstimate - GetCurrentMissionElapsed()).TotalMinutes));
        var warning = _services.GetService(typeof(WorkingDaySettings)) as WorkingDaySettings ?? new WorkingDaySettings();
        string? stage = null, title = null, severity = null, detail = null;
        var speak = false;
        if (minutesUntil <= 5)
        {
            stage = "5"; title = "TRANSITION REQUIRED"; severity = "High"; speak = true;
            detail = $"{CleanCalendarObjectiveTitle(next.Subject)} starts in {Math.Max(0, (int)Math.Ceiling(minutesUntil))} minutes. Current objective is still active with {remaining} minutes estimated remaining.";
        }
        else if (minutesUntil <= warning.TransitionWarningMinutes)
        {
            stage = "10"; title = $"NEXT BLOCK IN {Math.Max(0, (int)Math.Ceiling(minutesUntil))} MINUTES"; severity = "Warning";
            detail = $"{mission.Title} is still active. Next: {CleanCalendarObjectiveTitle(next.Subject)} at {next.Start:HH:mm}. Wrap up now, extend current work, or defer the next block.";
        }
        else if (remaining > minutesUntil)
        {
            stage = "15-fit"; title = "CURRENT OBJECTIVE WILL NOT FIT"; severity = "Warning";
            detail = $"{remaining} minutes of {mission.Title} remain, but {CleanCalendarObjectiveTitle(next.Subject)} starts in {(int)Math.Ceiling(minutesUntil)} minutes.";
        }
        if (stage is not null)
            EmitTransitionAttention(next, stage, title!, severity!, detail!, now, speak);
    }

    private void EmitTransitionAttention(CalendarEvent next, string stage, string title, string severity,
        string detail, DateTimeOffset now, bool speak)
    {
        var missionKey = Workspace.CurrentMission.TaskId?.ToString() ?? Workspace.CurrentMission.SourceRecordId ?? Workspace.CurrentMission.Title;
        var pairPrefix = $"calendar:transition:{missionKey}:{next.Id}:";
        var key = pairPrefix + stage;
        var alreadyEmitted = AttentionItems.Any(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ||
                             _resolvedAttentionKeys.ContainsKey(key);
        ResolveAttentionByPrefix(pairPrefix, key);
        RaiseAttention(key, "CALENDAR TRANSITION", severity, title, detail, "open_calendar", next.Id,
            next.WebLink, next.End.AddMinutes(5));
        if (speak && !alreadyEmitted) _ = SpeakGuardianAsync(detail, true);
    }
}
