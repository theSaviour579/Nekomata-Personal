namespace Nekomata.Models.Guardian;

public enum GuardianChaseStatus { Prepared, Approved, Sent, Cancelled, Failed }

public sealed class GuardianChaseAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OpenLoopId { get; set; }
    public int AttemptNumber { get; set; }
    public GuardianChaseStatus Status { get; set; } = GuardianChaseStatus.Prepared;
    public DateTimeOffset PreparedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string Recipient { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string ContextLabel { get; set; } = "";
    public Guid ProvenanceEventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset? LastSendAttemptAt { get; set; }
    public string LastSendError { get; set; } = "";
    public int SendAttemptCount { get; set; }
    public DateTimeOffset? NextChaseDueAt { get; set; }
}

public enum GuardianChaseState { NotDue, Due, Prepared, Approved, Sent, CoolingDown, EscalationDue }
