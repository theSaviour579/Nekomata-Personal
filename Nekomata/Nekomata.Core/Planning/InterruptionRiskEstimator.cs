using Nekomata.Models.Guardian;

namespace Nekomata.Core.Planning;

public sealed record ProtectedBlockCandidate(DateTimeOffset Start, DateTimeOffset End, int MeetingDensity);
public sealed record ProtectedBlockRisk(DateTimeOffset Start, DateTimeOffset End, int RiskPercent, string Label, IReadOnlyList<string> Evidence);

public sealed class InterruptionRiskEstimator
{
    public ProtectedBlockRisk? SelectBest(
        IEnumerable<ProtectedBlockCandidate> candidates,
        IReadOnlyCollection<GuardianInterruption> interruptions,
        IReadOnlyCollection<WorkdayEvent> journal,
        double recentReactivePercent)
    {
        return candidates.Select(candidate => Estimate(candidate, interruptions, journal, recentReactivePercent))
            .OrderBy(result => result.RiskPercent).ThenBy(result => result.Start).FirstOrDefault();
    }

    public ProtectedBlockRisk Estimate(
        ProtectedBlockCandidate candidate,
        IReadOnlyCollection<GuardianInterruption> interruptions,
        IReadOnlyCollection<WorkdayEvent> journal,
        double recentReactivePercent)
    {
        var evidence = new List<string>();
        var historical = interruptions.Where(item => item.InterruptedAt.DayOfWeek == candidate.Start.DayOfWeek &&
            Math.Abs(item.InterruptedAt.Hour - candidate.Start.Hour) <= 1).Count();
        var calls = journal.Count(item => item.EventType.Contains("PhoneCall", StringComparison.OrdinalIgnoreCase) &&
            item.OccurredAt.DayOfWeek == candidate.Start.DayOfWeek && Math.Abs(item.OccurredAt.Hour - candidate.Start.Hour) <= 1);
        var attention = journal.Count(item => item.EventType.Equals("AttentionRequired", StringComparison.OrdinalIgnoreCase) &&
            item.OccurredAt.DayOfWeek == candidate.Start.DayOfWeek && Math.Abs(item.OccurredAt.Hour - candidate.Start.Hour) <= 1);
        var score = 12 + Math.Min(30, historical * 8) + Math.Min(18, calls * 4) + Math.Min(18, attention * 4) +
                    Math.Min(18, candidate.MeetingDensity * 6) + (int)Math.Min(18, recentReactivePercent * .45);
        if (candidate.Start.Hour < 10) score -= 8;
        if (candidate.Start.Hour >= 14) score += 8;
        evidence.Add($"{historical} comparable interruption(s), {calls} call event(s), {attention} attention signal(s).");
        evidence.Add($"Meeting density {candidate.MeetingDensity}; recent reactive share {recentReactivePercent:0.#}%.");
        var risk = Math.Clamp(score, 5, 95);
        return new(candidate.Start, candidate.End, risk, risk < 30 ? "Low" : risk < 55 ? "Moderate" : "High", evidence);
    }
}
