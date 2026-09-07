using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions;

public static class NextActionPolicy
{
    public static string Key(MissionCandidate item) => $"{item.SourceType}:{item.SourceRecordId ?? item.TaskId?.ToString() ?? item.Title}";
    public static List<MissionCandidate> Select(IEnumerable<MissionCandidate> candidates, double freeMinutes, string ownName, ISet<string> excluded) => candidates
        .Where(x => freeMinutes >= 10 && x.IsActionable && !x.IsOnHold && !x.IsAwaitingExternalResponse &&
            !x.SourceType.Equals("Calendar", StringComparison.OrdinalIgnoreCase) &&
            x.EstimatedMinutes > 0 && x.EstimatedMinutes <= freeMinutes && !excluded.Contains(Key(x)))
        .Where(x => string.IsNullOrWhiteSpace(x.CurrentOwner) || x.CurrentOwner.Equals(ownName, StringComparison.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(x.SuggestedOwner) || x.SuggestedOwner.Equals(ownName, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(x => x.RequiresImmediateAttention || x.IsP1).ThenByDescending(x => x.Score)
        .DistinctBy(Key).ToList();
}
