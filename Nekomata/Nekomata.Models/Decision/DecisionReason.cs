namespace Nekomata.Models.Decision;

public class DecisionReason
{
    public string Title { get; set; } = "";
    public string Explanation { get; set; } = "";
    public int ScoreContribution { get; set; }
}