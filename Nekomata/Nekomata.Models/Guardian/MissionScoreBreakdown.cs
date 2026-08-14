namespace Nekomata.Models.Guardian;

public class MissionScoreBreakdown
{
    public int Commercial { get; set; }

    public int RevenueImpact { get; set; }

    public int CustomerImpact { get; set; }

    public int ExecutiveVisibility { get; set; }

    public int Automation { get; set; }

    public int FocusBonus { get; set; }

    public int SqlBonus { get; set; }

    public int InterruptiblePenalty { get; set; }

    public int RecurringPenalty { get; set; }

    public int DurationPenalty { get; set; }

    public int FinalScore { get; set; }

    public List<string> Positives { get; set; } = new();

    public List<string> Negatives { get; set; } = new();

    public string Recommendation { get; set; } = "";
}