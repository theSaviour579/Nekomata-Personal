using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions;

public interface IMissionOverrideService
{
    MissionOverrideResult Override(
        NekomataWorkspace workspace,
        GuardianRejectedMission alternative);
}