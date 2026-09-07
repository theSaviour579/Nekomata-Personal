using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions;

public sealed record AdaptiveReplanSuggestion(MissionCandidate Candidate, string Reason);

public static class AdaptiveReplanPolicy
{
    public static bool Same(Mission current, MissionCandidate candidate) =>
        current.SourceType.Equals(candidate.SourceType, StringComparison.OrdinalIgnoreCase) &&
        (!string.IsNullOrWhiteSpace(current.SourceRecordId) ? current.SourceRecordId == candidate.SourceRecordId :
         current.TaskId != null ? current.TaskId == candidate.TaskId : current.Title == candidate.Title);

    public static AdaptiveReplanSuggestion? Evaluate(Mission current, IEnumerable<MissionCandidate> candidates,
        double remainingMinutes, double freeMinutes, bool reportedBlocked)
    {
        if (freeMinutes < 5 || current.SourceType.Equals("Calendar", StringComparison.OrdinalIgnoreCase)) return null;
        var all = candidates.ToList();
        var currentEvidence = all.FirstOrDefault(x => Same(current, x));
        var blocked = reportedBlocked || currentEvidence is { IsActionable: false } || currentEvidence is { IsOnHold: true };
        var alternatives = all.Where(x => !Same(current, x) && x.IsActionable && !x.IsOnHold && !x.IsAwaitingExternalResponse &&
            x.EstimatedMinutes > 0 && x.EstimatedMinutes <= freeMinutes)
            .OrderByDescending(x => x.RequiresImmediateAttention).ThenByDescending(x => x.Score).ToList();
        var candidate = alternatives.FirstOrDefault();
        if (candidate == null) return null;
        var reason = blocked ? "Your current objective is blocked or no longer actionable." :
            candidate.RequiresImmediateAttention && currentEvidence?.RequiresImmediateAttention != true ? "Urgent work has arrived that fits your available time." :
            remainingMinutes > freeMinutes ? $"Your current objective needs about {Math.Ceiling(remainingMinutes)} minutes, but only {Math.Floor(freeMinutes)} minutes remain before the next protected boundary." : null;
        return reason == null ? null : new(candidate, reason);
    }
}
