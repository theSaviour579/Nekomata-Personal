namespace Nekomata.Models.Guardian;

public enum RequirementLevel { Required, Conditional, Recommended }
public enum RequirementState { NotApplicable, Required, InProgress, Satisfied, Waived, Blocked, EvidenceRequired }
public enum ReadinessGate { None, Deployment, Closure }
public enum RequirementEvidenceKind { Task, Chronicle, Approval, Document, UserDecision }

public sealed class ProcessRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkThreadId { get; set; }
    public string RuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string AppliesTo { get; set; } = "";
    public RequirementLevel Level { get; set; }
    public RequirementState State { get; set; } = RequirementState.Required;
    public ReadinessGate Gate { get; set; }
    public string Source { get; set; } = "";
    public string Reason { get; set; } = "";
    public bool RequiresTaskCompletion { get; set; }
    public bool RequiresEvidence { get; set; }
    public long? TaskId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<RequirementEvidence> Evidence { get; set; } = [];
}

public sealed class RequirementEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequirementId { get; set; }
    public RequirementEvidenceKind Kind { get; set; }
    public string Reference { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Satisfies { get; set; }
    public Guid? SourceEventId { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkThreadFact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkThreadId { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Source { get; set; } = "User";
    public Guid? SourceEventId { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
