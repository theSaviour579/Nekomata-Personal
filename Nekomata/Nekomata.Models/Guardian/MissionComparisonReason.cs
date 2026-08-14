namespace Nekomata.Models.Guardian;

public class MissionComparisonReason
{
    public string Category { get; set; } = "";

    public int WinnerPoints { get; set; }

    public int AlternativePoints { get; set; }

    public int Difference =>
        WinnerPoints - AlternativePoints;

    public string Explanation { get; set; } = "";
}