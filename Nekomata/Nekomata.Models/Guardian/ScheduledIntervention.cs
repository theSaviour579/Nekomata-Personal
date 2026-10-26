namespace Nekomata.Models.Guardian;

public enum ScheduledInterventionStatus
{
    Scheduled, Due, Prepared, AwaitingApproval, Completed, Cancelled, Superseded
}

public sealed class ScheduledIntervention
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PredictionId { get; set; }
    public Guid? WorkThreadId { get; set; }
    public string ActionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public GuardianActionAuthority Authority { get; set; }
    public ScheduledInterventionStatus Status { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset TriggerAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string PreparedContent { get; set; } = "";
    public string Outcome { get; set; } = "";
}
