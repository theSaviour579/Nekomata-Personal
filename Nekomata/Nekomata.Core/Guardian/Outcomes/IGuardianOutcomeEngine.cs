using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Outcomes;

public interface IGuardianOutcomeEngine
{
    Task ApplyLearningAsync(NekomataWorkspace workspace);
}
