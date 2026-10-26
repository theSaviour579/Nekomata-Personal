namespace Nekomata.Models.Guardian;

public enum AnticipationAuthority { Deterministic, PolicyBased, Learned, LlmAssisted }
public enum ExpectedWorkStatus { Candidate, Created, Satisfied, Dismissed, Superseded }
public enum ExpectedWorkDecisionType { Proposed, Created, Satisfied, Dismissed }

public sealed class ExpectedWorkCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkThreadId { get; set; }
    public string WorkType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public int EstimatedMinutes { get; set; }
    public decimal EstimatedBusinessValue { get; set; }
    public AnticipationAuthority Authority { get; set; }
    public double Confidence { get; set; }
    public bool ExistingWorkFound { get; set; }
    public Guid? SourceEventId { get; set; }
    public string SourceType { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public ExpectedWorkStatus Status { get; set; } = ExpectedWorkStatus.Candidate;
    public long? CreatedTaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExpectedWorkDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateId { get; set; }
    public ExpectedWorkDecisionType Decision { get; set; }
    public string Reason { get; set; } = "";
    public long? TaskId { get; set; }
    public Guid ProvenanceEventId { get; set; }
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
