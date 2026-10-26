using Nekomata.AI.Models.Meetings;

namespace Nekomata.Core.Meetings;
public static class MeetingNotesEvidence
{
    public static bool Supports(string notes, MeetingFollowThroughItem proposal) =>
        !string.IsNullOrWhiteSpace(proposal.Title) && proposal.Kind is "Action" or "Decision" &&
        !string.IsNullOrWhiteSpace(proposal.Evidence) && proposal.Evidence.Length >= 8 &&
        notes.Contains(proposal.Evidence, StringComparison.Ordinal);
}
