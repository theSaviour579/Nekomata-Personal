using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions;

public static class FreeWindowMissionPolicy
{
    public static MissionCandidate? Select(IEnumerable<MissionCandidate> candidates, int availableMinutes) => candidates
        .Where(item => item.IsActionable && !item.IsOnHold && !item.IsAwaitingExternalResponse)
        .Where(item => item.IsUrgent || item.RequiresImmediateAttention ||
                       EffectiveMinutes(item) <= Math.Max(0, availableMinutes))
        .OrderByDescending(item => item.IsUrgent || item.RequiresImmediateAttention)
        .ThenBy(item => item.Rank <= 0 ? int.MaxValue : item.Rank)
        .ThenByDescending(item => item.Score)
        .FirstOrDefault();

    public static int EffectiveMinutes(MissionCandidate candidate) => Math.Max(1,
        candidate.PredictedActiveMinutes > 0 ? candidate.PredictedActiveMinutes : candidate.EstimatedMinutes);
}
