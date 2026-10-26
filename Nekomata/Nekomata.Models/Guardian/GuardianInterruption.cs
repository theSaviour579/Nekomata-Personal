namespace Nekomata.Models.Guardian;

public sealed class GuardianInterruption
{
    public long Id { get; set; }
    public DateTimeOffset InterruptedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ResumedAt { get; set; }
    public string Description { get; set; } = "";
    public string Person { get; set; } = "";
    public string Decision { get; set; } = "Queued";
    public string Reason { get; set; } = "";
    public string MissionTitle { get; set; } = "";
    public long? TaskId { get; set; }
    public long? ProjectId { get; set; }
    public string WorkType { get; set; } = "General";
    public int ElapsedMinutes { get; set; }
    public int RemainingMinutes { get; set; }
    public int LostMinutes { get; set; }
    public string ResumeNote { get; set; } = "";
}

public sealed record InterruptionTriage(string Decision, string Headline, string Explanation, int ConfidencePercent);
