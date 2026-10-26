namespace Nekomata.Models.Guardian;

public enum GuardianDecisionDisposition { Proposed, Accepted, Rejected, Deferred, AutoExecuted }
public enum GuardianOutcomeDisposition { Pending, Successful, Mixed, Unsuccessful, Superseded }

public sealed class GuardianDecisionOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActionType { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public GuardianActionAuthority Authority { get; set; }
    public int Confidence { get; set; }
    public GuardianDecisionDisposition Decision { get; set; } = GuardianDecisionDisposition.Proposed;
    public GuardianOutcomeDisposition Outcome { get; set; } = GuardianOutcomeDisposition.Pending;
    public string DecisionReason { get; set; } = "";
    public string OutcomeEvidence { get; set; } = "";
    public int? OutcomeMinutes { get; set; }
    public Guid SourceEventId { get; set; }
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public DateTimeOffset RecommendedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
}
