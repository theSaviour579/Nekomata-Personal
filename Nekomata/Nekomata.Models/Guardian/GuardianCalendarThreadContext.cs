namespace Nekomata.Models.Guardian;

public sealed class GuardianCalendarThreadContext
{
    public string CalendarEventId { get; set; } = "";
    public Guid? ThreadId { get; set; }
    public WorkThreadLinkStatus LinkStatus { get; set; } = WorkThreadLinkStatus.Candidate;
    public int Confidence { get; set; }
    public long? TaskId { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsRecorded { get; set; }
    public bool MissingRequiredWork { get; set; }
    public int ResolvedOpenLoops { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class GuardianMissingWorkCandidate
{
    public string SourceType { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid? ThreadId { get; set; }
    public string Reason { get; set; } = "";
    public int Confidence { get; set; }
}
