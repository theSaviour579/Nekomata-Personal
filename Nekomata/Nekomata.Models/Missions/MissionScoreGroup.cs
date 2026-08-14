namespace Nekomata.Models.Missions;

public class MissionScoreGroup
{
    public string Category { get; init; } = "";

    public int TotalPoints { get; init; }

    public IReadOnlyList<MissionScoreFactor> Factors { get; init; }
        = [];
}