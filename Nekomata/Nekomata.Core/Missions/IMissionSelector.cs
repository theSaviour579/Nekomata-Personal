using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions;

public interface IMissionSelector
{
    IReadOnlyList<MissionCandidate> Rank(
       NekomataWorkspace workspace);
}