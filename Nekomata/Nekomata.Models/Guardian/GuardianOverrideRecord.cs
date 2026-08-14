namespace Nekomata.Models.Guardian;

public class GuardianOverrideRecord
{
    public DateTime RecordedAt { get; set; } =
        DateTime.Now;

    public string OriginalMissionTitle { get; set; } = "";

    public int OriginalMissionScore { get; set; }

    public decimal OriginalBusinessValue { get; set; }

    public int OriginalEstimatedMinutes { get; set; }

    public string SelectedMissionTitle { get; set; } = "";

    public int SelectedMissionScore { get; set; }

    public decimal SelectedBusinessValue { get; set; }

    public int SelectedEstimatedMinutes { get; set; }

    public int ScoreDifference =>
        SelectedMissionScore -
        OriginalMissionScore;

    public decimal BusinessValueDifference =>
        SelectedBusinessValue -
        OriginalBusinessValue;

    public int EstimatedMinutesDifference =>
        SelectedEstimatedMinutes -
        OriginalEstimatedMinutes;

    public string InferredPreference { get; set; } = "";

    public string Notes { get; set; } = "";
}