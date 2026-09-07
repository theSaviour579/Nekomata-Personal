namespace Nekomata.Models.Guardian;

public sealed class WorkdayEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public Guid? ParentEventId { get; set; }
    public Guid? CausedByEventId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.Now;
    public string EventType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Narrative { get; set; } = "";
    public int Significance { get; set; } = 50;
    public long? TaskId { get; set; }
    public long? ProjectId { get; set; }
    public string ExternalId { get; set; } = "";
    public Guid? RecommendationId { get; set; }
    public string Recommendation { get; set; } = "";
    public string UserDecision { get; set; } = "";
    public string Outcome { get; set; } = "";
    public decimal BusinessValue { get; set; }
    public string Metadata { get; set; } = "{}";
    public string TimeLabel => OccurredAt.LocalDateTime.ToString("HH:mm");
    public string DateLabel => OccurredAt.LocalDateTime.ToString("dddd dd MMMM yyyy");
    public bool HasProvenance => ParentEventId is not null || CausedByEventId is not null || RecommendationId is not null;
    public string CorrelationLabel => CorrelationId.ToString("N")[..8].ToUpperInvariant();
    public string ParentLabel => ParentEventId?.ToString("N")[..8].ToUpperInvariant() ?? "ROOT";
    public string CauseLabel => CausedByEventId?.ToString("N")[..8].ToUpperInvariant() ?? "DIRECT";
}

public sealed class GuardianJournalInsight
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
}
