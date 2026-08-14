using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Candidates;

public class IntegrationMissionCandidateProvider
    : IMissionCandidateProvider
{
    public IEnumerable<MissionCandidate> GetCandidates(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return workspace
            .IntegrationMissionCandidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(
                    candidate.Title))
            .Where(candidate => candidate.IsActionable)
            .ToList();
    }
}