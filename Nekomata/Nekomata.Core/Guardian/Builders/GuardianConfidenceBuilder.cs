using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianConfidenceBuilder
{
    public int Calculate(
        NekomataWorkspace workspace,
        IReadOnlyCollection<GuardianReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(reasons);

        var confidence = 35;

        if (workspace.CurrentMission is not null)
            confidence += 20;

        if (workspace.Tasks.Count > 0)
            confidence += 10;

        if (workspace.Projects.Count > 0)
            confidence += 10;

        if (!workspace.Capacity.IsOverCapacity)
            confidence += 10;

        if (workspace.Briefing.MissionsCompletedYesterday > 0)
            confidence += 5;

        var openTasks =
            workspace.Tasks
                .Where(task => !task.Completed)
                .ToList();

        if (openTasks.Count > 0)
        {
            var datedTaskPercentage =
                openTasks.Count(task => task.DueAt is not null) /
                (double)openTasks.Count;

            confidence +=
                (int)Math.Round(
                    datedTaskPercentage * 10);
        }

        if (reasons.Any(reason => !reason.Positive))
            confidence -= 5;

        return Math.Clamp(
            confidence,
            0,
            100);
    }
}