using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.WorkThreads;

public sealed record GuardianCorrelationEvaluation(int Confidence, WorkThreadLinkStatus Status, string Explanation);

public static class GuardianCorrelationPolicy
{
    public const int ConfirmationThreshold = 90;

    public static GuardianCorrelationEvaluation Evaluate(IEnumerable<GuardianCorrelationEvidence> evidence)
    {
        // Repeated observations in one evidence family cannot inflate confidence.
        var independent = evidence
            .GroupBy(x => x.EvidenceType, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.Weight).First())
            .OrderByDescending(x => x.Weight)
            .ToList();
        var score = Math.Min(100, independent.Sum(x => Math.Max(0, x.Weight)));
        var status = score >= ConfirmationThreshold ? WorkThreadLinkStatus.Confirmed : WorkThreadLinkStatus.Candidate;
        var explanation = independent.Count == 0
            ? "No active correlation evidence."
            : string.Join(" + ", independent.Select(x => $"{x.EvidenceType} ({x.Weight})"));
        return new(score, status, explanation);
    }
}
