using System.Security.Cryptography;
using System.Text;

namespace Nekomata.Core.Analytics;

public enum WorkDebtStatus { Observing, Emerging, Actionable, ImprovementPlanned, Resolved, AcceptedCost, Dismissed }

public sealed record WorkDebtItem(
    Guid Id, string Title, string ClusterKey, string Category,
    DateTime FirstObservedAt, DateTime LastObservedAt, int EpisodeCount,
    int TotalTimeMinutes, int OvertimeMinutes, int? EstimatedRecurringAnnualMinutes,
    int EstimatedRemediationMinutes, decimal EstimatedLabourCost, decimal EstimatedRemediationCost,
    string StrategicRelevance, ValueEvidenceKind ValueType, int Confidence, WorkDebtStatus Status,
    string Recommendation, string EvidenceSummary, DateTime CreatedAt, DateTime UpdatedAt)
{
    public bool CanPrepareImprovement => Status == WorkDebtStatus.Actionable;
    public string ReviewActionLabel => CanPrepareImprovement ? "PREPARE IMPROVEMENT" : "REVIEW EVIDENCE";
    public string CostLabel => $"{EstimatedLabourCost:C0} observed · {EstimatedRemediationCost:C0} estimated remediation";
    public string RecurrenceLabel => EstimatedRecurringAnnualMinutes is int annual
        ? $"~{annual}m annual recurrence estimate"
        : "Annual recurrence not yet estimated";
}

public sealed record WorkDebtImprovementProposal(
    string Title, string Description, string ClusterKey, int EstimatedMinutes,
    int EstimatedAnnualMinutesAvoided, decimal EstimatedAnnualLabourSaving, int Confidence);

public static class WorkDebtImprovementProposalBuilder
{
    public static WorkDebtImprovementProposal? Build(WorkDebtItem item, decimal hourlyLabourRate)
    {
        if (!item.CanPrepareImprovement) return null;
        var annualRecurringMinutes = item.EstimatedRecurringAnnualMinutes ?? item.TotalTimeMinutes;
        var avoidableMinutes = Math.Max(0, annualRecurringMinutes - item.EstimatedRemediationMinutes);
        var saving = Math.Round(avoidableMinutes / 60m * Math.Max(0, hourlyLabourRate), 2,
            MidpointRounding.AwayFromZero);
        var description = $"""
            Guardian work-debt improvement proposal for: {item.Title}

            Evidence: {item.EvidenceSummary}
            Assessment: {item.Recommendation}

            Investigate the root cause and compare automation, delegation, documentation and accepted-cost options. Preserve necessary controls. Record the chosen intervention and measure whether recurring minutes reduce afterwards.

            Estimated investigation/remediation effort: {item.EstimatedRemediationMinutes} minutes.
            Estimated annual recurrence avoided if the intervention succeeds: {avoidableMinutes} minutes.
            Estimated annual labour saving: {saving:C} at the configured hourly rate.
            Evidence confidence: {item.Confidence}%.
            """;
        return new($"Reduce recurring work: {item.ClusterKey}", description, item.ClusterKey,
            item.EstimatedRemediationMinutes, avoidableMinutes, saving, item.Confidence);
    }
}

public sealed record WorkDebtAnalysis(IReadOnlyList<WorkDebtItem> Items)
{
    public IReadOnlyList<WorkDebtItem> Actionable => Items.Where(x => x.Status == WorkDebtStatus.Actionable).ToList();
    public int TotalMinutes => Items.Sum(x => x.TotalTimeMinutes);
    public int OvertimeMinutes => Items.Sum(x => x.OvertimeMinutes);
}

public interface IWorkDebtRepository
{
    Task UpsertAsync(IReadOnlyCollection<WorkDebtItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkDebtItem>> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task SetStatusAsync(Guid id, WorkDebtStatus status, CancellationToken cancellationToken = default);
}

public sealed class WorkDebtEngine
{
    private static readonly string[] OperationalCategories =
        ["Operational support", "Administration & communication", "Operational resilience", "Rework / avoidable"];
    private static readonly string[] NecessaryControlTerms =
        ["compliance", "audit", "control", "governance"];
    private static readonly string[] AvoidableTerms =
        ["remediation", "rework", "stale", "failure", "error", "firefight"];

    public WorkDebtAnalysis Analyse(IEnumerable<WorkEpisode> source, decimal hourlyLabourRate, DateTime? observedAt = null)
    {
        var now = observedAt ?? DateTime.Now;
        var items = source
            .DistinctBy(x => x.Key)
            .Where(IsPotentialDebt)
            .GroupBy(x => NormaliseCluster(x.Cluster), StringComparer.OrdinalIgnoreCase)
            .Select(group => Build(group.Key, group.OrderBy(x => x.StartedAt).ToList(), hourlyLabourRate, now))
            .Where(x => x is not null)
            .Cast<WorkDebtItem>()
            .OrderByDescending(x => x.Status == WorkDebtStatus.Actionable)
            .ThenByDescending(x => x.TotalTimeMinutes)
            .ToList();
        return new(items);
    }

    private static bool IsPotentialDebt(WorkEpisode episode) =>
        OperationalCategories.Contains(episode.Category, StringComparer.OrdinalIgnoreCase) ||
        episode.OperatingMode is "Reactive work" or "Administrative overhead" or "Rework / avoidable";

    private static WorkDebtItem? Build(string key, IReadOnlyList<WorkEpisode> episodes, decimal rate, DateTime now)
    {
        if (episodes.Count < 2) return null;
        var text = string.Join(' ', episodes.Select(x => x.Title)).ToLowerInvariant();
        var necessaryControl = NecessaryControlTerms.Any(text.Contains);
        var avoidableEvidence = episodes.All(x => AvoidableTerms.Any(term => x.Title.Contains(term, StringComparison.OrdinalIgnoreCase)));
        var total = episodes.Sum(x => x.ActiveMinutes);
        var overtime = episodes.Sum(x => x.AdditionalMinutes);
        var average = Math.Max(15, (int)Math.Round(episodes.Average(x => x.ActiveMinutes)));
        var spanDays = Math.Max(1, (episodes[^1].StartedAt.Date - episodes[0].StartedAt.Date).Days + 1);
        var sufficientForAnnualisation = episodes.Count >= 4 && spanDays >= 21;
        var annual = sufficientForAnnualisation
            ? (int)Math.Round(total / (double)spanDays * 365, MidpointRounding.AwayFromZero)
            : (int?)null;
        var remediation = Math.Clamp(average * 3, 90, 480);
        var confidence = Math.Clamp(30 + episodes.Count * 7 + Math.Min(20, spanDays / 3), 35, 92);
        if (necessaryControl || !avoidableEvidence) confidence = Math.Min(confidence, 55);
        var status = necessaryControl || !avoidableEvidence ? WorkDebtStatus.Observing
            : episodes.Count >= 3 && total >= remediation && confidence >= 60 ? WorkDebtStatus.Actionable
            : episodes.Count >= 3 ? WorkDebtStatus.Emerging : WorkDebtStatus.Observing;
        var title = key.EndsWith("operations", StringComparison.OrdinalIgnoreCase) ? key : $"{key} recurring work";
        var recommendation = necessaryControl
            ? "This appears to be a necessary recurring control. Observe its delivery cost, but do not treat repetition alone as avoidable work."
            : !avoidableEvidence ? "Repetition alone is not evidence of avoidable work. Obtain episode-specific root-cause evidence before proposing remediation."
            : status == WorkDebtStatus.Actionable
                ? $"Based on observed episodes, a root-cause or automation investigation estimated at roughly {remediation} minutes may now cost less than continued recurrence. Prepare an improvement mission for approval."
                : "This pattern may be emerging as recurring work debt. Continue observing before committing remediation capacity.";
        var evidence = $"{episodes.Count} distinct episodes consumed {total} minutes ({overtime} outside working hours) between {episodes[0].StartedAt:dd MMM} and {episodes[^1].StartedAt:dd MMM}. " +
                       (annual is null ? "There is not yet enough history for a responsible annual estimate." : $"Observed recurrence annualises to an estimated {annual} minutes; this is a projection, not a measured outcome.");
        evidence += DescribeObservedWork(episodes);
        return new(DeterministicId(key), title, key, episodes[0].Category,
            episodes[0].StartedAt, episodes[^1].FinishedAt, episodes.Count, total, overtime, annual,
            remediation, Cost(total, rate), Cost(remediation, rate),
            episodes.OrderByDescending(x => RelevanceRank(x.StrategicRelevance)).First().StrategicRelevance,
            episodes.OrderByDescending(x => x.ValueConfidence).First().ValueEvidence,
            confidence, status, recommendation, evidence, episodes[0].StartedAt, now);
    }

    private static decimal Cost(int minutes, decimal rate) => Math.Round(minutes / 60m * Math.Max(0, rate), 2);
    private static string DescribeObservedWork(IReadOnlyList<WorkEpisode> episodes)
    {
        var groupedTitles = episodes
            .GroupBy(
                x => string.IsNullOrWhiteSpace(x.Title) ? "Untitled work" : x.Title.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visibleTitles = groupedTitles
            .Take(5)
            .Select(x => x.Count() == 1 ? x.Key : $"{x.Key} ({x.Count()} episodes)");
        var remainingTitles = groupedTitles.Count - 5;
        return " Observed work: " + string.Join("; ", visibleTitles) +
               (remainingTitles > 0 ? $"; +{remainingTitles} more work type(s)." : ".");
    }

    private static int RelevanceRank(string value) => value.Equals("High", StringComparison.OrdinalIgnoreCase) ? 3 : value.Equals("Medium", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    private static string NormaliseCluster(string value) => string.IsNullOrWhiteSpace(value) ? "Unclassified operational work" : value.Trim();
    public static Guid DeterministicId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("work-debt:" + key.ToLowerInvariant()))[..16]);
}
