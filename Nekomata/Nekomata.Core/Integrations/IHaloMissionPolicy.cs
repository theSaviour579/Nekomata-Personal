using Nekomata.Models.Integrations;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Integrations.Halo;

public interface IHaloMissionPolicy
{
    void Apply(
        IntegrationMission mission,
        MissionCandidate candidate);
}