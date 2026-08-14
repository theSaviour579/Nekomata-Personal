namespace Nekomata.Models.Missions;

public class MissionScoreFactor
{
    public string Name { get; set; } = "";

    public string Explanation { get; set; } = "";

    public int Points { get; set; }

    public bool IsPositive => Points >= 0;

    public string Category { get; set; } = "";
}