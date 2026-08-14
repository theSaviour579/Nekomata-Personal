using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Evidence;

public interface IGuardianEvidenceBuilder
{
    GuardianEvidence Build(
        NekomataWorkspace workspace);
}