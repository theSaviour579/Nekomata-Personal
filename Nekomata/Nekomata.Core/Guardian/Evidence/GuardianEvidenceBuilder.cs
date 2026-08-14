using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Evidence;

public class GuardianEvidenceBuilder
    : IGuardianEvidenceBuilder
{
    public GuardianEvidence Build(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var now =
            DateTime.Now;

        var openTasks =
            workspace.Tasks
                .Where(task => !task.Completed)
                .ToList();

        var mission =
            workspace.CurrentMission;

        var evidence =
            new GuardianEvidence
            {
                // Mission

                HasMission =
                mission is not null,

            MissionScore =
                mission?.Score ?? 0,

            MissionBusinessValue =
                mission?.BusinessValue ?? 0,

            MissionDuration =
                mission?.EstimatedDuration
                ?? TimeSpan.Zero,

            // Tasks

            OpenTasks =
                openTasks.Count,

            DueToday =
                openTasks.Count(task =>
                    task.DueAt is not null &&
                    task.DueAt.Value.Date == now.Date),

            Overdue =
                openTasks.Count(task =>
                    task.DueAt is not null &&
                    task.DueAt.Value < now),

            Undated =
                openTasks.Count(task =>
                    task.DueAt is null),

            Critical =
                openTasks.Count(task =>
                    string.Equals(
                        task.Priority,
                        "Critical",
                        StringComparison.OrdinalIgnoreCase)),

                // Capacity

                OverCapacity =
    workspace.Briefing.IsOverCapacity,

                CapacityMinutesRemaining =
    workspace.Briefing.RemainingCapacityMinutes,

                CapacitySummary =
    workspace.Briefing.CapacitySummary,

                // Projects

                ActiveProjects =
                workspace.Projects.Count(project =>
                    !string.Equals(
                        project.Status,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase)),

            // History

            MissionsCompletedYesterday =
                workspace.Briefing
                    .MissionsCompletedYesterday,

            // Integrations
            // These remain false until the connectors exist.

            HaloConnected = false,
            CalendarConnected = false,
            SqlConnected = false,
            EmailConnected = false
        };
        System.Diagnostics.Debug.WriteLine(
    $"EVIDENCE OVER CAPACITY: {evidence.OverCapacity}");

        System.Diagnostics.Debug.WriteLine(
            $"EVIDENCE REMAINING: {evidence.CapacityMinutesRemaining}");

        System.Diagnostics.Debug.WriteLine(
            $"EVIDENCE SUMMARY: {evidence.CapacitySummary}");
        //-------------------------------------------------------
        // Workspace Health
        //-------------------------------------------------------

        var health = 100;

        if (evidence.Overdue > 0)
        {
            health -= 10;

            evidence.HealthWarnings.Add(
                evidence.Overdue == 1
                    ? "1 task is overdue."
                    : $"{evidence.Overdue} tasks are overdue.");
        }

        if (evidence.Undated > 0)
        {
            health -= 10;

            evidence.HealthWarnings.Add(
                evidence.Undated == 1
                    ? "1 task has no due date."
                    : $"{evidence.Undated} tasks have no due date.");
        }

        if (evidence.OverCapacity)
        {
            health -= 15;

            evidence.HealthWarnings.Add(
                "Today's workload exceeds available capacity.");
        }

        if (!evidence.CalendarConnected)
        {
            health -= 5;

            evidence.HealthWarnings.Add(
                "Calendar integration is unavailable.");
        }

        if (!evidence.HaloConnected)
        {
            health -= 5;

            evidence.HealthWarnings.Add(
                "Halo integration is unavailable.");
        }

        if (!evidence.SqlConnected)
        {
            health -= 5;

            evidence.HealthWarnings.Add(
                "SQL integration is unavailable.");
        }

        if (!evidence.EmailConnected)
        {
            health -= 5;

            evidence.HealthWarnings.Add(
                "Email integration is unavailable.");
        }

        evidence.WorkspaceHealthScore =
            Math.Clamp(
                health,
                0,
                100);

        return evidence;
    }
}