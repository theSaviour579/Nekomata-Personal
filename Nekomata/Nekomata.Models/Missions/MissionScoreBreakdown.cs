namespace Nekomata.Models.Missions;

public class MissionScoreBreakdown
{
    public int BusinessValue { get; set; }

    public int Urgency { get; set; }

    public int Risk { get; set; }

    public int Priority { get; set; }

    public int Effort { get; set; }

    public int Bonus { get; set; }

    public int Total =>
        BusinessValue +
        Urgency +
        Risk +
        Priority +
        Effort +
        Bonus;
}