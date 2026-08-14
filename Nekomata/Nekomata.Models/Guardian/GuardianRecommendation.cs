namespace Nekomata.Models.Guardian;

public class GuardianRecommendation
{
    public string Summary { get; set; } = "";

    public string Text { get; set; } = "";

    public List<string> Reasons { get; set; } = [];

    public GuardianAdvice Advice { get; set; } = new();
}