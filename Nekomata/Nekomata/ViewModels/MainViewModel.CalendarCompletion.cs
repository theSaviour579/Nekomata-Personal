using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Missions;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private async Task<string?> ReconcileCompletedCalendarMissionAsync(
        Mission mission,
        DateTimeOffset completedAt)
    {
        var calendarEvent = FindCalendarEventForMission(mission, completedAt);
        if (calendarEvent is null || !calendarEvent.IsNekomataManaged ||
            completedAt <= calendarEvent.Start || completedAt >= calendarEvent.End)
            return null;

        var calendar = _services.GetRequiredService<ICalendarService>();
        await calendar.MoveFocusEventAsync(calendarEvent.Id, calendarEvent.Start, completedAt);

        var message = $"Calendar block shortened to {completedAt:HH:mm}.";
        var next = CalendarEvents
            .Where(item => item.Start >= calendarEvent.End && item.IsNekomataManaged && !item.IsAllDay)
            .OrderBy(item => item.Start)
            .FirstOrDefault();

        var bestUnscheduled = Workspace.RankedMissionCandidates
            .Where(item => item.TaskId != mission.TaskId &&
                           !CalendarTitlesMatch(calendarEvent.Subject, item.Title))
            .OrderByDescending(item => item.RequiresImmediateAttention)
            .ThenBy(item => item.Rank)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();

        if (next is not null)
        {
            var nextCandidate = Workspace.RankedMissionCandidates.FirstOrDefault(item =>
                CalendarTitlesMatch(next.Subject, item.Title) ||
                (ResolveCalendarTaskId(next) is long taskId && item.TaskId == taskId));
            var duration = next.End - next.Start;
            var proposedEnd = completedAt + duration;
            var protectedConflict = CalendarEvents.Any(item =>
                item.Id != next.Id && !item.IsNekomataManaged &&
                (item.IsAllDay || item.Start < proposedEnd && item.End > completedAt));

            if (!protectedConflict && nextCandidate is not null &&
                (bestUnscheduled is null || nextCandidate.Score >= bestUnscheduled.Score))
            {
                await calendar.MoveFocusEventAsync(next.Id, completedAt, proposedEnd);
                return $"{message} Guardian pulled '{CleanCalendarObjectiveTitle(next.Subject)}' forward to {completedAt:HH:mm}.";
            }
        }

        return bestUnscheduled is null
            ? message
            : $"{message} Guardian recommends '{bestUnscheduled.Title}' for the newly available time.";
    }

    private CalendarEvent? FindCalendarEventForMission(Mission mission, DateTimeOffset completedAt)
    {
        if (!string.IsNullOrWhiteSpace(mission.SourceRecordId))
        {
            var byId = CalendarEvents.FirstOrDefault(item => item.Id == mission.SourceRecordId);
            if (byId is not null) return byId;
        }

        return CalendarEvents.FirstOrDefault(item =>
            item.Start <= completedAt && item.End > completedAt &&
            (CalendarTitlesMatch(item.Subject, mission.Title) ||
             (mission.TaskId is long taskId && ResolveCalendarTaskId(item) == taskId)));
    }
}
