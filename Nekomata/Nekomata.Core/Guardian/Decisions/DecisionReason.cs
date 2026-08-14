namespace Nekomata.Core.Guardian.Decisions;

public class DecisionReason
{
    public string Title { get; set; } = "";

    public int Score { get; set; }

    public string Explanation { get; set; } = "";
}