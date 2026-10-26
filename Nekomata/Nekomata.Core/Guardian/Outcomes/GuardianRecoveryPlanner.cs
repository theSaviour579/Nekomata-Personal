using Nekomata.Models.Common;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class GuardianRecoveryPlanner
{
    public GuardianRecoveryPlan? Build(NekomataWorkspace workspace, GuardianInterruption interruption, DateTime now)
    {
        if (interruption.LostMinutes < 5 || interruption.RemainingMinutes <= 0) return null;
        var protectedBlock = workspace.Timeline.FirstOrDefault(x =>
            !x.IsFixed && x.Title.Contains(interruption.MissionTitle, StringComparison.OrdinalIgnoreCase));
        var windowRemaining = protectedBlock is null ? interruption.RemainingMinutes - interruption.LostMinutes :
            Math.Max(0, (int)(protectedBlock.EndAt - now).TotalMinutes);
        var overrun = Math.Max(0, interruption.RemainingMinutes - windowRemaining);
        var displacement = workspace.RankedMissionCandidates
            .Where(x => x.TaskId is > 0 && x.TaskId != interruption.TaskId)
            .Where(x => !x.RequiresImmediateAttention && !x.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.DueAt?.Date <= now.Date)
            .OrderBy(x => x.Score).ThenBy(x => x.BusinessValue)
            .FirstOrDefault();
        if (displacement is null) return null;

        var recovered = Math.Max(1, displacement.EstimatedMinutes);
        var finish = workspace.Capacity.ExpectedFinishAt.AddMinutes(-Math.Min(recovered,
            interruption.LostMinutes + workspace.Capacity.ExpectedOvertimeMinutes));
        var preserved = workspace.Timeline.Where(x => x.IsFixed && x.EndAt > now)
            .OrderBy(x => x.StartAt).Take(4).Select(x => $"{x.StartAt:HH:mm} {x.Title}").ToList();
        return new GuardianRecoveryPlan
        {
            MinutesLost = interruption.LostMinutes, ProtectedObjective = interruption.MissionTitle,
            ProtectedObjectiveRemainingMinutes = interruption.RemainingMinutes, ProtectedWindowOverrunMinutes = overrun,
            DeferredTaskId = displacement.TaskId!.Value, DeferredTaskTitle = displacement.Title,
            MinutesRecovered = recovered, DeferredUntil = NextWorkingDay(now), ExpectedFinishAt = finish,
            PreservedCommitments = preserved,
            ImpactSummary = overrun > 0
                ? $"{interruption.LostMinutes} minutes lost. {interruption.MissionTitle} now exceeds its protected window by approximately {overrun} minutes."
                : $"{interruption.LostMinutes} minutes lost. Remaining capacity has tightened around {interruption.MissionTitle}.",
            RecoverySummary = $"Keep {interruption.MissionTitle}. Move lower-ranked {displacement.Title} to tomorrow and recover {recovered} minutes."
        };
    }

    public GuardianRecoveryPlan? BuildFromForecast(NekomataWorkspace workspace, GuardianWorkloadForecast forecast, DateTime now)
    {
        if (!forecast.NeedsRecovery) return null;
        var displacement = workspace.RankedMissionCandidates
            .Where(x => x.TaskId is > 0 && x.DueAt?.Date <= now.Date)
            .Where(x => x.TaskId != workspace.CurrentMission?.TaskId || x.ProjectId != workspace.CurrentMission?.ProjectId)
            .Where(x => !x.RequiresImmediateAttention && !x.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Score).ThenBy(x => x.BusinessValue).FirstOrDefault();
        if (displacement is null) return null;
        var recovered = Math.Max(1, displacement.PredictedActiveMinutes > 0
            ? displacement.PredictedActiveMinutes + displacement.InterruptionOverheadMinutes
            : displacement.EstimatedMinutes);
        var remainingRisk = Math.Max(0, forecast.MinutesAtRisk - recovered);
        var protectedTitle = string.IsNullOrWhiteSpace(workspace.CurrentMission?.Title) ? "today's highest-value work" : workspace.CurrentMission.Title;
        return new()
        {
            ForecastDriven = true, ForecastConfidence = forecast.ConfidencePercent,
            MinutesLost = forecast.InterruptionAllowanceMinutes, ProtectedObjective = protectedTitle,
            DeferredTaskId = displacement.TaskId!.Value, DeferredTaskTitle = displacement.Title,
            MinutesRecovered = recovered, DeferredUntil = NextWorkingDay(now),
            ExpectedFinishAt = forecast.ProjectedFinishAt.LocalDateTime.AddMinutes(-Math.Min(recovered, forecast.MinutesAtRisk)),
            PreservedCommitments = workspace.Timeline.Where(x => x.IsFixed && x.EndAt > now)
                .OrderBy(x => x.StartAt).Take(4).Select(x => $"{x.StartAt:HH:mm} {x.Title}").ToList(),
            ImpactSummary = $"Guardian forecasts {forecast.MinutesAtRisk} minutes at risk with {forecast.ConfidencePercent}% confidence." +
                (forecast.ThreatenedCommitments.Count == 0 ? "" : $" Likely to slip: {string.Join(", ", forecast.ThreatenedCommitments.Take(3))}."),
            RecoverySummary = remainingRisk == 0
                ? $"Move lower-ranked {displacement.Title} to {NextWorkingDay(now):dddd} and recover {recovered} minutes. The forecast returns within workable capacity."
                : $"Move lower-ranked {displacement.Title} to {NextWorkingDay(now):dddd} and recover {recovered} minutes. Approximately {remainingRisk} minutes would still require review."
        };
    }

    private static DateTime NextWorkingDay(DateTime now)
    {
        var result = now.Date.AddDays(1);
        while (result.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) result = result.AddDays(1);
        return result;
    }
}
