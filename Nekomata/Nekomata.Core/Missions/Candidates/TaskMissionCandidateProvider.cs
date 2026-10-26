using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Candidates;

public class TaskMissionCandidateProvider
    : IMissionCandidateProvider
{
    public IEnumerable<MissionCandidate> GetCandidates(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return workspace.Tasks
            .Where(task =>
                string.Equals(task.Status, "Open", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.Status, "On Hold", StringComparison.OrdinalIgnoreCase))
            .Select(task => new MissionCandidate
            {
                SourceType = "Task",
                IsOnHold = string.Equals(task.Status, "On Hold", StringComparison.OrdinalIgnoreCase),

                TaskId = task.Id,
                ProjectId = task.ProjectId,

                Title = task.Title,
                Description = task.Description ?? "",

                Priority = task.Priority,
                // PriorityScore is already composite. Mission scoring adds its
                // explicit priority/value factors, so using it here double-counts.
                BaseScore = 0,
                Score = 0,

                BusinessValue =
                    task.EstimatedBusinessValue,

                EstimatedMinutes =
                    Math.Max((task.PredictedCalendarMinutes > 0 ? task.PredictedCalendarMinutes : task.EstimatedMinutes) - task.ActualMinutes, 1),
                EnteredEstimateMinutes = task.OriginalEstimatedMinutes > 0 ? task.OriginalEstimatedMinutes : task.EstimatedMinutes,
                PredictedActiveMinutes = task.PredictedActiveMinutes > 0 ? task.PredictedActiveMinutes : task.EstimatedMinutes,
                InterruptionOverheadMinutes = task.ExpectedInterruptionOverheadMinutes,
                PredictionExplanation = task.EstimateLearningExplanation,

                DueAt = task.DueAt,

                Progress = task.EstimatedMinutes <= 0
                    ? 0
                    : Math.Clamp(
                        task.ActualMinutes / (double)task.EstimatedMinutes,
                        0,
                        1),

                AtRisk =
                    task.DueAt is not null &&
                    task.DueAt.Value < DateTime.Now,

                RecommendationReason =
                    BuildReason(task),
                Strengths =
    task.ScoreBreakdown?.Positives?.ToList() ?? [],

                Risks =
    task.ScoreBreakdown?.Negatives?.ToList() ?? [],

                GuardianDecision =
    task.ScoreBreakdown?.Recommendation
    ?? $"Complete {task.Title} before lower-priority work.",
            });
    }

    private static string BuildReason(
        Nekomata.Models.Tasks.NekomataTask task)
    {
        var reasons = new List<string>();

        if (task.BusinessCritical)
            reasons.Add("business critical");

        if (task.DueAt is not null)
        {
            var days =
                (task.DueAt.Value.Date - DateTime.Today).Days;

            if (days < 0)
                reasons.Add("overdue");
            else if (days == 0)
                reasons.Add("due today");
            else if (days <= 3)
                reasons.Add($"due in {days} days");
        }

        if (task.EstimatedBusinessValue > 0)
        {
            reasons.Add(
                $"estimated value " +
                $"{task.EstimatedBusinessValue:C0}");
        }

        if (task.RequiresFocus)
            reasons.Add("requires focused work");

        if (!string.IsNullOrWhiteSpace(task.EstimateLearningExplanation))
            reasons.Add(task.EstimateLearningExplanation);

        return reasons.Count == 0
            ? "Highest current task priority."
            : string.Join(", ", reasons);
    }
}
