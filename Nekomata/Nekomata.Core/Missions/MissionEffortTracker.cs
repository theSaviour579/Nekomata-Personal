using Nekomata.Models.Tasks;

namespace Nekomata.Core.Missions;

public static class MissionEffortTracker
{
    public static MissionEffortUpdate ApplyElapsed(
        NekomataTask task,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(task);

        var workedMinutes = elapsed <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Ceiling(elapsed.TotalMinutes));
        var estimate = Math.Max(task.EstimatedMinutes, 1);
        task.ActualMinutes = Math.Clamp(
            task.ActualMinutes + workedMinutes,
            0,
            estimate);

        return new MissionEffortUpdate(
            workedMinutes,
            Math.Max(0, estimate - task.ActualMinutes),
            Math.Clamp(task.ActualMinutes / (double)estimate, 0, 1));
    }
}

public sealed record MissionEffortUpdate(
    int WorkedMinutes,
    int RemainingMinutes,
    double Progress);