using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Workspace;

// A read-only projection for summaries. External items retain their source
// identity in IntegrationMissionCandidates and are ranked there only once.
internal static class WorkspaceTaskSummary
{
    internal static IEnumerable<NekomataTask> GetTasks(NekomataWorkspace workspace) =>
        workspace.Tasks.Concat(workspace.IntegrationMissionCandidates
            .Where(item => item.IsActionable &&
                (item.SourceType.Equals("Microsoft Planner", StringComparison.OrdinalIgnoreCase) ||
                 item.SourceType.Equals("Microsoft To Do", StringComparison.OrdinalIgnoreCase)))
            .Where(item => item.TaskId is null || !workspace.Tasks.Any(task => task.Id == item.TaskId))
            .Select(item => new NekomataTask
            {
                Title = item.Title, Source = item.SourceType, Description = item.Description,
                Priority = item.Priority, PriorityScore = item.Score, DueAt = item.DueAt,
                EstimatedMinutes = item.EstimatedMinutes, EstimatedBusinessValue = item.BusinessValue
            }));
}
