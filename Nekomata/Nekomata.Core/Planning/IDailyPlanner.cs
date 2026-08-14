using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public interface IDailyPlanner
{
    NekomataWorkspace BuildPlan(NekomataWorkspace workspace);
}