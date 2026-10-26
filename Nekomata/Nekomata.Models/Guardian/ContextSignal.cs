namespace Nekomata.Models.Guardian;

public enum ContextSignalKind { AssignedWork, Conversation, Meeting, Email, Presence }
public enum ContextSignalDisposition { New, Duplicate, Correlated, NeedsReview, Superseded }

public sealed class ContextSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Source { get; set; } = "";
    public ContextSignalKind Kind { get; set; }
    public string ExternalId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Person { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid SourceEventId { get; set; }
    public ContextSignalDisposition Disposition { get; set; } = ContextSignalDisposition.New;
    public Guid? WorkThreadId { get; set; }
    public long? ExistingTaskId { get; set; }
    public string Resolution { get; set; } = "";
}

public sealed class GuardianNewCommitment
{
    public Guid SignalId { get; set; }
    public string Source { get; set; } = "";
    public string Title { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTimeOffset? DueAt { get; set; }
    public Guid? WorkThreadId { get; set; }
    public string Reason { get; set; } = "";
}
