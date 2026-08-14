namespace Nekomata.Models.Guardian;

public class MissionSimulation
{
    public string MissionTitle { get; set; } = "";

    public int Score { get; set; }

    public int ScoreDifference { get; set; }

    public decimal BusinessValue { get; set; }

    public decimal BusinessValueDifference { get; set; }

    public int EstimatedMinutes { get; set; }

    public int EstimatedMinutesDifference { get; set; }

    public int Confidence { get; set; }

    public int ConfidenceDifference { get; set; }

    public bool ExceedsCapacity { get; set; }

    public int CapacityImpactMinutes { get; set; }

    public string RiskLevel { get; set; } = "Low";

    public string Summary { get; set; } = "";

    public List<string> Benefits { get; } = [];

    public List<string> Consequences { get; } = [];

    public string ScoreDifferenceFormatted =>
    ScoreDifference switch
    {
        > 0 => $"+{ScoreDifference}",
        < 0 => ScoreDifference.ToString(),
        _ => "0"
    };

    public string BusinessValueDifferenceFormatted =>
        BusinessValueDifference switch
        {
            > 0 => $"+{BusinessValueDifference:C0}",
            < 0 => $"-{Math.Abs(BusinessValueDifference):C0}",
            _ => "£0"
        };

    public string EstimatedMinutesDifferenceFormatted =>
        EstimatedMinutesDifference switch
        {
            > 0 => $"+{EstimatedMinutesDifference} mins",
            < 0 => $"{EstimatedMinutesDifference} mins",
            _ => "0 mins"
        };

    public string Verdict { get; set; } = "";

    public bool RecommendSwitch { get; set; }

    public string DecisionIcon =>
    RecommendSwitch
        ? "↗"
        : "✓";

    public string DecisionTitle =>
    RecommendSwitch
        ? "SWITCH TO ALTERNATIVE"
        : "KEEP CURRENT MISSION";

    public string DecisionSubtitle =>
        RecommendSwitch
            ? $"Today's objective should become {MissionTitle}."
            : "Today's objective remains unchanged.";

    public string DecisionMission =>
        RecommendSwitch
            ? MissionTitle
            : "Current Mission";
}