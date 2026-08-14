namespace Nekomata.Models.Guardian;

public class GuardianDashboardRecommendation
{
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }

    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RecommendationType { get; set; } = "Project";

    public int Score { get; set; }
    public decimal BusinessValue { get; set; }
    public int EstimatedMinutes { get; set; }
    public string Priority { get; set; } = "Normal";
    public DateTime? DueAt { get; set; }
    public bool AtRisk { get; set; }
    public int ProgressPercent { get; set; }
}