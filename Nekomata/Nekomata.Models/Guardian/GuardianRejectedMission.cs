namespace Nekomata.Models.Guardian;

public class GuardianRejectedMission
{
    public string Title { get; set; } = "";

    public string SourceType { get; set; } = "";

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public int Score { get; set; }

    public decimal BusinessValue { get; set; }

    public int EstimatedMinutes { get; set; }

    public DateTime? DueAt { get; set; }

    public double Progress { get; set; }

    public string ThreatLevel { get; set; } = "";

    public string WhyNot { get; set; } = "";

    public int Rank { get; set; }

    public string RecommendationReason { get; set; } = "";

    public int ScoreDifference { get; set; }

    public List<MissionComparisonReason> ComparisonReasons { get; set; }
        = [];
}