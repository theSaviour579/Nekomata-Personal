using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Recommendations;

public class GuardianRecommendationService
    : IGuardianRecommendationService
{
    public GuardianDashboardRecommendation? GetTopRecommendation(
        NekomataWorkspace workspace)
    {
        var projectRecommendations = workspace.Projects
            .Where(project =>
                !string.Equals(
                    project.Status,
                    "Completed",
                    StringComparison.OrdinalIgnoreCase))
            .Select(BuildProjectRecommendation);

        var taskRecommendations = workspace.Tasks
            .Where(task =>
                string.Equals(
                    task.Status,
                    "Open",
                    StringComparison.OrdinalIgnoreCase))
            .Select(BuildTaskRecommendation);

        return projectRecommendations
            .Concat(taskRecommendations)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.BusinessValue)
            .FirstOrDefault();
    }

    private static GuardianDashboardRecommendation BuildProjectRecommendation(
        NekomataProject project)
    {
        var score = 0;

        score += PriorityScore(project.Priority);

        score += project.AtRisk ? 25 : 0;

        score += DueDateScore(project.DueAt);

        score += BusinessValueScore(
            project.EstimatedBusinessValue);

        score += ProgressScore(project.ProgressPercent);

        var reasonParts = new List<string>();

        if (project.AtRisk)
            reasonParts.Add("the project is marked at risk");

        if (project.DueAt is not null)
        {
            var daysRemaining =
                (project.DueAt.Value.Date - DateTime.Today).Days;

            if (daysRemaining < 0)
                reasonParts.Add("the deadline has passed");
            else if (daysRemaining <= 7)
                reasonParts.Add(
                    $"it is due in {daysRemaining} day" +
                    $"{(daysRemaining == 1 ? "" : "s")}");
        }

        if (project.EstimatedBusinessValue > 0)
        {
            reasonParts.Add(
                $"it carries an estimated business value of " +
                $"{project.EstimatedBusinessValue:C0}");
        }

        if (project.ProgressPercent >= 70 &&
            project.ProgressPercent < 100)
        {
            reasonParts.Add(
                $"it is already {project.ProgressPercent}% complete");
        }

        var reason = reasonParts.Count == 0
            ? "It currently has the strongest overall priority score."
            : $"Recommended because {string.Join(", ", reasonParts)}.";

        return new GuardianDashboardRecommendation
        {
            ProjectId = project.Id,
            Title = project.Name,
            Reason = reason,
            RecommendationType = "Project",
            Score = score,
            BusinessValue = project.EstimatedBusinessValue,
            EstimatedMinutes =
                project.EstimatedRemainingMinutes,
            Priority = project.Priority,
            DueAt = project.DueAt,
            AtRisk = project.AtRisk,
            ProgressPercent = project.ProgressPercent
        };
    }

    private static GuardianDashboardRecommendation BuildTaskRecommendation(
        NekomataTask task)
    {
        var score = task.PriorityScore;

        score += PriorityScore(task.Priority);

        score += DueDateScore(task.DueAt);

        score += BusinessValueScore(
            task.EstimatedBusinessValue);

        score += task.BusinessCritical ? 20 : 0;
        score += task.RequiresFocus ? 5 : 0;

        var reasonParts = new List<string>();

        if (task.BusinessCritical)
            reasonParts.Add("it is business critical");

        if (task.DueAt is not null)
        {
            var daysRemaining =
                (task.DueAt.Value.Date - DateTime.Today).Days;

            if (daysRemaining < 0)
                reasonParts.Add("it is overdue");
            else if (daysRemaining <= 3)
                reasonParts.Add(
                    $"it is due in {daysRemaining} day" +
                    $"{(daysRemaining == 1 ? "" : "s")}");
        }

        if (task.EstimatedBusinessValue > 0)
        {
            reasonParts.Add(
                $"it carries an estimated value of " +
                $"{task.EstimatedBusinessValue:C0}");
        }

        var reason = reasonParts.Count == 0
            ? "It currently has the strongest task priority score."
            : $"Recommended because {string.Join(", ", reasonParts)}.";

        return new GuardianDashboardRecommendation
        {
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            Title = task.Title,
            Reason = reason,
            RecommendationType = "Task",
            Score = score,
            BusinessValue = task.EstimatedBusinessValue,
            EstimatedMinutes = task.EstimatedMinutes,
            Priority = task.Priority,
            DueAt = task.DueAt
        };
    }

    private static int PriorityScore(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "critical" => 40,
            "high" => 25,
            "normal" => 10,
            "low" => 5,
            _ => 0
        };
    }

    private static int DueDateScore(DateTime? dueAt)
    {
        if (dueAt is null)
            return 0;

        var days =
            (dueAt.Value.Date - DateTime.Today).Days;

        return days switch
        {
            < 0 => 40,
            0 => 35,
            <= 2 => 25,
            <= 7 => 15,
            <= 14 => 5,
            _ => 0
        };
    }

    private static int BusinessValueScore(decimal value)
    {
        if (value <= 0) return 0;
        if (value < 10000m) return 5;
        var points = 12 + (int)Math.Round(18 * Math.Log10((double)(value / 10000m)));
        return Math.Clamp(points, 12, 55);
    }

    private static int ProgressScore(int progress)
    {
        return progress switch
        {
            >= 90 and < 100 => 20,
            >= 70 => 12,
            >= 40 => 6,
            _ => 0
        };
    }
}