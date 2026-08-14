using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Learning;

public interface IGuardianLearningService
{
    GuardianOverrideRecord RecordOverride(
        Mission originalMission,
        Mission selectedMission,
        NekomataWorkspace workspace);
}