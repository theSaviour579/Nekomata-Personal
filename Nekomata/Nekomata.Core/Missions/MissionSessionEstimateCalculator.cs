namespace Nekomata.Core.Missions;

public static class MissionSessionEstimateCalculator
{
    public static TimeSpan Calculate(
        TimeSpan taskEffortRemaining,
        bool isScheduledNow,
        DateTimeOffset now,
        DateTimeOffset? activeBlockEnd)
    {
        var taskEstimate = taskEffortRemaining > TimeSpan.Zero
            ? taskEffortRemaining
            : TimeSpan.FromMinutes(1);

        if (!isScheduledNow ||
            activeBlockEnd is null ||
            activeBlockEnd <= now)
        {
            return taskEstimate;
        }

        var available = activeBlockEnd.Value - now;
        return available < taskEstimate ? available : taskEstimate;
    }
}