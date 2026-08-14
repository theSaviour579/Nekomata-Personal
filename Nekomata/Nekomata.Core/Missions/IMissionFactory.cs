using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions;

public interface IMissionFactory
{
    Mission Create(
        MissionCandidate candidate);
}