using Nekomata.Core.Analytics.Models;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Analytics.Capacity;

public sealed class DailyCapacityCalculator : IDailyCapacityCalculator
{
    public DailyCapacity Calculate(NekomataWorkspace workspace)
    {
        var summary = workspace.Capacity;
        var capacity = new DailyCapacity
        {
            AvailableMinutes = summary.AvailableMinutesToday,
            PlannedMinutes = summary.PlannedMinutesToday,
            RemainingMinutes = summary.RemainingMinutesToday,
            OverCapacityMinutes = summary.OverCapacityMinutes,
            UtilisationPercent = summary.AvailableMinutesToday == 0
                ? (summary.PlannedMinutesToday > 0 ? 100 : 0)
                : Math.Min(100, summary.PlannedMinutesToday * 100d / summary.AvailableMinutesToday),
            MissionMinutes = Math.Max(0, (int)workspace.CurrentMission.EstimatedDuration.TotalMinutes),
            DueTodayMinutes = workspace.Tasks.Where(task => !task.IsCompleted && task.DueAt?.Date == DateTime.Today).Sum(task => Math.Max(0, task.EstimatedMinutes - task.ActualMinutes)),
            OverdueMinutes = workspace.Tasks.Where(task => !task.IsCompleted && task.DueAt?.Date < DateTime.Today).Sum(task => Math.Max(0, task.EstimatedMinutes - task.ActualMinutes)),
            ScheduledMinutes = summary.ScheduledMinutesRemaining
        };
        return capacity;
    }
}