using Nekomata.Models.AI;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Tasks;

public interface IGuardianTaskPlanningService
{
    Task<GuardianTaskActionPlan> BuildPlanAsync(
        string instruction,
        NekomataWorkspace workspace);
}