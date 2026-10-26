namespace Nekomata.Models.Guardian;

public sealed class WorkflowObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkThreadId { get; set; }
    public string TriggerType { get; set; } = "";
    public string ResultWorkType { get; set; } = "";
    public bool Observed { get; set; }
    public string Evidence { get; set; } = "";
    public Guid? SourceEventId { get; set; }
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearnedWorkflowPattern
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TriggerType { get; set; } = "";
    public string ResultWorkType { get; set; } = "";
    public int ComparableThreadCount { get; set; }
    public int SupportThreadCount { get; set; }
    public int ExceptionThreadCount { get; set; }
    public int ConfidencePercent { get; set; }
    public string Lifecycle { get; set; } = "Observing";
    public Guid? GuardianPolicyId { get; set; }
    public DateTimeOffset FirstObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PolicyPromotionDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatternId { get; set; }
    public string Decision { get; set; } = "";
    public string Rationale { get; set; } = "";
    public Guid? GuardianPolicyId { get; set; }
    public Guid SourceEventId { get; set; }
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}
