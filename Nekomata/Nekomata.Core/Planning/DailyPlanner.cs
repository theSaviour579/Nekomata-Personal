using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public class DailyPlanner : IDailyPlanner
{
    public NekomataWorkspace BuildPlan(NekomataWorkspace workspace)
    {
        var current = DateTime.Today.AddHours(8);

        foreach (var task in workspace.Tasks
                     .OrderByDescending(x => x.PriorityScore))
        {
            task.StartAt = current;
            task.FinishAt = current.AddMinutes(task.EstimatedMinutes);

            current = task.FinishAt ?? current;
        }

        return workspace;
    }
}