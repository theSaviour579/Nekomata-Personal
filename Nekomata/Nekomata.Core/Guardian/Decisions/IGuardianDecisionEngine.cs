using Nekomata.Models.Workspace;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Decisions;

public interface IGuardianDecisionEngine
{
    GuardianDecision Analyse(
        NekomataWorkspace workspace);
}