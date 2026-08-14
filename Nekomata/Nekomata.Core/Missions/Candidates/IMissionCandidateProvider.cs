using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Candidates;

public interface IMissionCandidateProvider
{
    IEnumerable<MissionCandidate> GetCandidates(
        NekomataWorkspace workspace);
}