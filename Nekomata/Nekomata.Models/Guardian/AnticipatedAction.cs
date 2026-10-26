namespace Nekomata.Models.Guardian;

public enum AnticipatedActionStatus { Proposed, Prepared, Approved, ApprovalRequired, ReadyToExecute, Waiting, Sent, Failed, Expired, Completed, Cancelled }

public sealed class AnticipatedAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TriggerType { get; set; } = "";
    public string TriggerKey { get; set; } = "";
    public Guid? WorkThreadId { get; set; }
    public string ProposedAction { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public int Confidence { get; set; }
    public string Impact { get; set; } = "";
    public GuardianActionAuthority Authority { get; set; }
    public AnticipatedActionStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExecuteAfter { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid SourceEventId { get; set; }
    public DateTimeOffset? SuggestedStart { get; set; }
    public DateTimeOffset? SuggestedEnd { get; set; }
    public string PreparedContent { get; set; } = "";
    public string TargetConversationId { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? LastSendAttemptAt { get; set; }
    public string LastSendError { get; set; } = "";
}
