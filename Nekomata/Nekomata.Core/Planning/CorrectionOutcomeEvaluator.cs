using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Planning;

public sealed class RoleCorrectionOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public RoleCorrectionType Type { get; set; }
    public string BandKey { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public double ExpectedAlignmentImprovement { get; set; }
    public RoleCorrectionDecision Decision { get; set; } = RoleCorrectionDecision.Proposed;
    public bool Occurred { get; set; }
    public double? ObservedAlignmentChange { get; set; }
    public bool ExternalDisruptionPrevented { get; set; }
    public int AlignmentAtRecommendation { get; set; }
    public DateTimeOffset RecommendedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ExpectedOccurrenceAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public List<string> Evidence { get; set; } = [];
}

public sealed class CorrectionOutcomeEvaluator(
    IRoleCorrectionStore store,
    IGuardianDecisionOutcomeRepository decisionLearning)
{
    public async Task<IReadOnlyList<RoleCorrectionOutcome>> GetRecentAsync(int count = 100,CancellationToken cancellationToken=default) =>
        (await store.LoadAsync(cancellationToken)).OrderByDescending(item => item.RecommendedAt).Take(count).ToList();

    public async Task<Guid> RecordRecommendationAsync(RoleCorrectiveAction action, int currentAlignment, string source, bool deduplicateDaily = true)
    {
        var values = (await store.LoadAsync()).ToList();
        var existing = deduplicateDaily
            ? values.FirstOrDefault(item => item.Type == action.Type && item.BandKey == action.BandKey &&
                item.RecommendedAt.Date == DateTimeOffset.Now.Date)
            : null;
        if (existing is not null) return existing.Id;
        var record = new RoleCorrectionOutcome
        {
            Id = action.Id, Type = action.Type, BandKey = action.BandKey, Recommendation = action.Title,
            ExpectedAlignmentImprovement = action.ExpectedAlignmentImprovement,
            AlignmentAtRecommendation = currentAlignment,
            ExpectedOccurrenceAt = action.SuggestedBlock?.End ?? DateTimeOffset.Now.AddDays(1),
            Evidence = action.Evidence.Concat([$"Source: {source}"]).ToList()
        };
        await store.UpsertAsync(record);
        await decisionLearning.UpsertAsync(ToDecisionOutcome(record));
        return record.Id;
    }

    public async Task RecordDecisionAsync(Guid id, RoleCorrectionDecision decision, string reason, bool occurred = false)
    {
        var values = (await store.LoadAsync()).ToList();
        var record = values.FirstOrDefault(item => item.Id == id);
        if (record is null) return;
        record.Decision = decision; record.DecidedAt = DateTimeOffset.Now; record.Occurred |= occurred; record.Evidence.Add(reason);
        if (occurred) record.ExpectedOccurrenceAt = DateTimeOffset.Now;
        await store.UpsertAsync(record);
        var outcome = ToDecisionOutcome(record);
        outcome.Decision = decision switch
        {
            RoleCorrectionDecision.Approved => GuardianDecisionDisposition.Accepted,
            RoleCorrectionDecision.Rejected => GuardianDecisionDisposition.Rejected,
            _ => GuardianDecisionDisposition.Proposed
        };
        outcome.DecisionReason = reason;
        outcome.DecidedAt = record.DecidedAt;
        if (decision == RoleCorrectionDecision.Rejected) outcome.Outcome = GuardianOutcomeDisposition.Superseded;
        await decisionLearning.UpsertAsync(outcome);
    }

    public async Task EvaluateAsync(int currentAlignment, IReadOnlyCollection<WorkdayEvent> recentEvents)
    {
        var values = (await store.LoadAsync()).ToList();
        foreach (var record in values.Where(item => item.Decision == RoleCorrectionDecision.Approved && item.ObservedAt is null &&
            DateTimeOffset.Now >= (item.ExpectedOccurrenceAt ?? item.RecommendedAt).AddHours(4)))
        {
            record.ObservedAlignmentChange = currentAlignment - record.AlignmentAtRecommendation;
            record.ExternalDisruptionPrevented = recentEvents.Any(entry => entry.OccurredAt >= record.RecommendedAt &&
                (entry.EventType.Contains("PhoneCall", StringComparison.OrdinalIgnoreCase) ||
                 entry.Narrative.Contains("urgent", StringComparison.OrdinalIgnoreCase) || entry.Narrative.Contains("outage", StringComparison.OrdinalIgnoreCase)));
            record.Occurred |= recentEvents.Any(entry => entry.RecommendationId == record.Id &&
                (entry.Outcome.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                 entry.Outcome.Contains("applied", StringComparison.OrdinalIgnoreCase) ||
                 entry.EventType.Contains("Completed", StringComparison.OrdinalIgnoreCase)));
            record.ObservedAt = DateTimeOffset.Now;
            record.Evidence.Add($"Observed alignment change: {record.ObservedAlignmentChange:+0.#;-0.#;0} points." +
                                (record.ExternalDisruptionPrevented ? " External disruption was present; causation is not inferred." :
                                 " The change is associated with the recommendation window; causation is not proven."));
            var outcome = ToDecisionOutcome(record);
            outcome.Outcome = record.Occurred && record.ObservedAlignmentChange > 0 ? GuardianOutcomeDisposition.Successful :
                record.Occurred ? GuardianOutcomeDisposition.Mixed :
                record.ExternalDisruptionPrevented ? GuardianOutcomeDisposition.Mixed : GuardianOutcomeDisposition.Unsuccessful;
            outcome.OutcomeEvidence = record.Evidence.Last(); outcome.ObservedAt = record.ObservedAt;
            await decisionLearning.UpsertAsync(outcome);
            await store.UpsertAsync(record);
        }
    }

    private static GuardianDecisionOutcome ToDecisionOutcome(RoleCorrectionOutcome record) => new()
    {
        Id = record.Id, ActionType = $"RoleCorrection:{record.Type}", Recommendation = record.Recommendation,
        Authority = GuardianActionAuthority.Prepare, Confidence = 70,
        Decision = record.Decision == RoleCorrectionDecision.Approved ? GuardianDecisionDisposition.Accepted :
            record.Decision == RoleCorrectionDecision.Rejected ? GuardianDecisionDisposition.Rejected : GuardianDecisionDisposition.Proposed,
        Outcome = record.Occurred ? GuardianOutcomeDisposition.Successful : GuardianOutcomeDisposition.Pending,
        DecisionReason = string.Join(" ", record.Evidence.TakeLast(2)), OutcomeEvidence = record.ObservedAlignmentChange is null ? "" : $"Alignment changed {record.ObservedAlignmentChange:+0.#;-0.#;0} points.",
        RecommendedAt = record.RecommendedAt, DecidedAt = record.DecidedAt, ObservedAt = record.ObservedAt
    };
}
