using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class GuardianDayReviewService(
    IMissionSessionRepository sessions,
    IGuardianInterruptionRepository interruptions)
{
    public async Task<GuardianDayReview> BuildAsync(NekomataWorkspace workspace)
    {
        var today = await sessions.GetTodayAsync();
        var disruptions = await interruptions.GetTodayAsync();
        return Build(workspace, today, disruptions);
    }

    public static GuardianDayReview Build(
        NekomataWorkspace workspace,
        IReadOnlyCollection<MissionSession> sessions,
        IReadOnlyCollection<GuardianInterruption> interruptions)
    {
        var completed = sessions.Where(x => x.Completed).ToList();
        var deep = sessions.Where(x => IsDeepWork(x.SourceType, x.Title)).ToList();
        var strongest = sessions.GroupBy(x => x.StartedAt.Hour)
            .Where(x => x.Count() >= 2)
            .Select(x => new { Hour = x.Key, Survival = x.Count(y => y.Completed) / (double)x.Count(), Count = x.Count() })
            .OrderByDescending(x => x.Survival).ThenByDescending(x => x.Count).ThenBy(x => x.Hour).FirstOrDefault();
        var bestLearning = workspace.Outcomes.Insights
            .Where(x => x.Kind is "DurationAccuracy" or "FragmentationCost" or "FocusWindow")
            .OrderByDescending(x => x.ConfidencePercent).FirstOrDefault();
        var rollForward = workspace.Tasks.Count(x => !x.IsCompleted && x.DueAt?.Date <= DateTime.Today);

        return new GuardianDayReview
        {
            PlannedMinutes = Math.Max(workspace.Capacity.ScheduledFocusMinutesToday, workspace.Capacity.PlannedMinutesToday),
            CompletedMinutes = completed.Sum(x => x.ActualDurationMinutes),
            InterruptionCount = interruptions.Count,
            InterruptionMinutes = interruptions.Sum(x => x.LostMinutes),
            UnplannedMinutes = completed.Where(x => x.SourceType is "Email" or "Teams").Sum(x => x.ActualDurationMinutes),
            FocusBlocksProtected = deep.Count(x => x.Completed), FocusBlocksPlanned = deep.Count,
            ObjectivesCompleted = completed.Count, ObjectivesPlanned = sessions.Count,
            RollForwardCount = rollForward,
            LearnedSummary = bestLearning?.Summary ?? "Not enough evidence for a new personal correction today.",
            StrongestWindowStart = strongest is null ? null : TimeSpan.FromHours(strongest.Hour),
            TomorrowSummary = strongest is null
                ? $"{rollForward} unfinished commitment{(rollForward == 1 ? "" : "s")} should roll forward. More focus evidence is needed to identify the strongest window."
                : $"{rollForward} unfinished commitment{(rollForward == 1 ? "" : "s")} should roll forward. {strongest.Hour:00}:00–{strongest.Hour + 1:00}:30 is currently the strongest observed focus window ({strongest.Survival:P0} survival)."
        };
    }

    private static bool IsDeepWork(string source, string title)
    {
        var value = $"{source} {title}".ToLowerInvariant();
        return value.Contains("analysis") || value.Contains("sql") || value.Contains("project") || value.Contains("deep");
    }
}
