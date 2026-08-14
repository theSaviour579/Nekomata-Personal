using Nekomata.Models.Common;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Candidates;

public class ProjectMissionCandidateProvider
    : IMissionCandidateProvider
{
    private const int ProjectBaseScore = 35;

    public IEnumerable<MissionCandidate> GetCandidates(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        foreach (var project in workspace.Projects)
        {
            System.Diagnostics.Debug.WriteLine(
                $"{project.Id} | {project.Name} | Status={project.Status} | Progress={project.ProgressPercent}%");
        }
        return workspace.Projects
            .Where(IsActive)
            .Select(CreateCandidate);
    }

    private static MissionCandidate CreateCandidate(
        NekomataProject project)
    {
        var isOverdue =
            IsOverdue(project);

        var atRisk =
            project.AtRisk ||
            isOverdue;

        return new MissionCandidate
        {
            SourceType =
                "Project",


            IsOnHold = string.Equals(project.Status, "On Hold", StringComparison.OrdinalIgnoreCase),

            LastUpdatedAt = project.UpdatedAt == default ? null : project.UpdatedAt,
            ProjectId =
                project.Id,

            TaskId =
                null,

            Title =
                string.IsNullOrWhiteSpace(project.NextAction)
                    ? $"{project.Name} · Define next action"
                    : $"{project.Name} · Next action",

            Description =
                string.IsNullOrWhiteSpace(project.NextAction)
                    ? "Define and record the next executable action for this project."
                    : project.NextAction,

            Priority =
                string.IsNullOrWhiteSpace(
                    project.Priority)
                    ? TaskPriorities.Normal
                    : project.Priority,

            /*
             * Projects receive a strategic baseline because
             * they represent wider outcomes rather than one
             * isolated execution task.
             *
             * Business value, urgency, risk and progress are
             * added separately by MissionCandidateScorer.
             */
            BaseScore =
                CalculateBaseScore(project),

            Score =
                0,

            // One focus block does not deliver the entire project. Attribute a
            // bounded share while retaining total strategic exposure separately.
            BusinessValue =
                CalculateAttributableValue(project),

            StrategicBusinessValue =
                project.EstimatedBusinessValue,

            EstimatedMinutes =
                string.IsNullOrWhiteSpace(project.NextAction)
                    ? 20
                    : Math.Clamp(project.EstimatedRemainingMinutes, 15, 120),

            DueAt =
                project.DueAt,

            Progress =
                Math.Clamp(
                    project.ProgressPercent / 100.0,
                    0,
                    1),

            AtRisk =
                atRisk,

            RecommendationReason =
                BuildReason(project)
        };
    }

    private static decimal CalculateAttributableValue(NekomataProject project)
    {
        if (project.EstimatedBusinessValue <= 0) return 0;
        if (string.IsNullOrWhiteSpace(project.NextAction))
            return Math.Min(project.EstimatedBusinessValue * 0.05m, 50000m);
        return Math.Min(project.EstimatedBusinessValue * 0.10m, 100000m);
    }

    private static int CalculateBaseScore(
        NekomataProject project)
    {
        var score =
            ProjectBaseScore;

        /*
         * These adjustments represent project-level
         * strategic context rather than duplicating the
         * shared deadline or business-value scoring.
         */

        if (!string.IsNullOrWhiteSpace(
                project.NextAction))
        {
            // The project has a clearly actionable next step.
            score += 5;
        }

        if (project.AtRisk)
        {
            // Strategic oversight is more important when
            // the whole project is explicitly marked at risk.
            score += 10;
        }

        return Math.Clamp(
            score,
            0,
            60);
    }

    private static bool IsActive(
        NekomataProject project)
    {
        return
            !string.Equals(
                project.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase)
            &&
            !string.Equals(
                project.Status,
                "Cancelled",
                StringComparison.OrdinalIgnoreCase)
            &&
            !string.Equals(
                project.Status,
                "Closed",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverdue(
        NekomataProject project)
    {
        return
            project.DueAt is not null
            &&
            project.DueAt.Value <
            DateTime.Now;
    }

    private static string BuildReason(
        NekomataProject project)
    {
        var reasons =
            new List<string>();

        if (project.AtRisk)
        {
            reasons.Add(
                "marked at risk");
        }

        if (project.DueAt is not null)
        {
            var daysRemaining =
                (project.DueAt.Value.Date -
                 DateTime.Today).Days;

            if (daysRemaining < 0)
            {
                reasons.Add(
                    "overdue");
            }
            else if (daysRemaining == 0)
            {
                reasons.Add(
                    "due today");
            }
            else if (daysRemaining <= 7)
            {
                reasons.Add(
                    $"due in {daysRemaining} day" +
                    $"{(daysRemaining == 1 ? "" : "s")}");
            }
        }

        if (project.EstimatedBusinessValue > 0)
        {
            reasons.Add(
                $"protects a project with {project.EstimatedBusinessValue:C0} total strategic value; " +
                $"this action is attributed {CalculateAttributableValue(project):C0}");
        }

        if (project.ProgressPercent >= 70 &&
            project.ProgressPercent < 100)
        {
            reasons.Add(
                $"{project.ProgressPercent}% complete");
        }

        if (!string.IsNullOrWhiteSpace(
                project.NextAction))
        {
            reasons.Add(
                $"next action: {project.NextAction}");
        }

        return reasons.Count == 0
            ? "Active strategic project requiring oversight."
            : string.Join(
                ", ",
                reasons);
    }
}