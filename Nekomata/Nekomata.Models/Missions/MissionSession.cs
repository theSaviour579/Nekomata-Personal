namespace Nekomata.Models.Missions;

public class MissionSession
{
    public long Id { get; set; }

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public string Title { get; set; } = "";

    public string SourceType { get; set; } = "";

    public int Score { get; set; }

    public decimal BusinessValue { get; set; }

    public int EstimatedDurationMinutes { get; set; }

    public int ActualDurationMinutes { get; set; }

    public TimeSpan EstimatedDuration =>
        TimeSpan.FromMinutes(EstimatedDurationMinutes);

    public TimeSpan ActualDuration =>
        TimeSpan.FromMinutes(ActualDurationMinutes);

    public DateTime StartedAt { get; set; }

    public DateTime FinishedAt { get; set; }

    public bool Completed { get; set; }

    public bool Cancelled { get; set; }

    public string GuardianDecision { get; set; } = "";

    public string RecommendationReason { get; set; } = "";
}