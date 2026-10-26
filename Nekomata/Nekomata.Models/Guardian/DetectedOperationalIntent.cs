namespace Nekomata.Models.Guardian;

public enum OperationalIntentType { ScheduleMeeting }

public sealed class DetectedOperationalIntent
{
    public Guid Id { get; set; }
    public Guid SignalId { get; set; }
    public OperationalIntentType Type { get; set; }
    public string Person { get; set; } = "";
    public int DurationMinutes { get; set; }
    public string RequestedWindow { get; set; } = "";
    public Guid? WorkThreadId { get; set; }
    public int Confidence { get; set; }
    public string EvidenceSummary { get; set; } = "";
    public string TargetConversationId { get; set; } = "";
    public DateTimeOffset ObservedAt { get; set; }
}
