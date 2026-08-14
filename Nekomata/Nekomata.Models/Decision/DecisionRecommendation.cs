namespace Nekomata.Models.Decision;

public class DecisionRecommendation
{
    public string Summary { get; set; } = "";

    public List<DecisionReason> Reasons { get; set; } = [];

    public string NextAction { get; set; } = "";

    public string? DelegateTo { get; set; }

    public bool ShouldDelegate =>
        !string.IsNullOrWhiteSpace(DelegateTo);
}