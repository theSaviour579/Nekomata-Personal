using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions.Scoring;

public interface IMissionCandidateScorer
{
    void Score(MissionCandidate candidate);
}