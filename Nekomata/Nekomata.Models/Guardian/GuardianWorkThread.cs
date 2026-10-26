namespace Nekomata.Models.Guardian;

public enum WorkThreadLinkAuthority { Explicit, Deterministic, Semantic, LlmAssisted }
public enum WorkThreadLinkStatus { Candidate, Confirmed, Rejected, Superseded }

public sealed class GuardianWorkThread
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Active";
    public long? PrimaryProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GuardianWorkThreadLink> Links { get; set; } = [];
}

public sealed class GuardianWorkThreadLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string RelationshipType { get; set; } = "BelongsTo";
    public int Confidence { get; set; }
    public WorkThreadLinkAuthority Authority { get; set; }
    public string LinkMethod { get; set; } = "";
    public WorkThreadLinkStatus Status { get; set; } = WorkThreadLinkStatus.Candidate;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid SourceEventId { get; set; }
    public List<GuardianCorrelationEvidence> Evidence { get; set; } = [];
}

public sealed class GuardianCorrelationEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LinkId { get; set; }
    public string EvidenceType { get; set; } = "";
    public string Value { get; set; } = "";
    public int Weight { get; set; }
    public Guid SourceEventId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GuardianUnboundContext
{
    public string EntityType { get; set; } = "";
    public string EntityKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "No confirmed Work Thread relationship.";
}
