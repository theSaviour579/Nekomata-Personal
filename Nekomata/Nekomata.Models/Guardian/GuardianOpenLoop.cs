namespace Nekomata.Models.Guardian;

public enum OpenLoopType { AwaitingThem, AwaitingMe, MeetingRequired, DecisionRequired, ApprovalRequired, InformationRequired, FollowUpRequired }
public enum OpenLoopStatus { Open, Resolved, Superseded, Cancelled }

public sealed class GuardianOpenLoop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public OpenLoopType Type { get; set; } = OpenLoopType.FollowUpRequired;
    public OpenLoopStatus Status { get; set; } = OpenLoopStatus.Open;
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string WaitingOn { get; set; } = "";
    public string WaitingOnAddress { get; set; } = "";
    public DateTimeOffset? DueAt { get; set; }
    public long? PersonId { get; set; }
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public string EmailId { get; set; } = "";
    public string PersonAddress { get; set; } = "";
    public string TeamsMessageId { get; set; } = "";
    public string CalendarEventId { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceExternalId { get; set; } = "";
    public Guid SourceEventId { get; set; }
    public string ExpectedEvidenceKind { get; set; } = "";
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolutionEventId { get; set; }
    public string ResolutionSummary { get; set; } = "";
}

public sealed class GuardianLoopEvidence
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public Guid? OpenLoopId { get; set; }
    public long? PersonId { get; set; }
    public long? TaskId { get; set; }
    public long? ProjectId { get; set; }
    public string EmailId { get; set; } = "";
    public string TeamsMessageId { get; set; } = "";
    public string CalendarEventId { get; set; } = "";
    public string SourceExternalId { get; set; } = "";
}

public enum OpenLoopEvidenceDisposition { NoMatch, Resolved, NeedsReview }
public sealed record OpenLoopEvidenceResult(GuardianOpenLoop OpenLoop, OpenLoopEvidenceDisposition Disposition, string Reason);
