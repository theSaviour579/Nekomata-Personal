namespace Nekomata.Models.AI;

public class AiRecommendation
{
    public string Summary { get; set; } = "No recommendation generated yet.";
    public string SuggestedNextAction { get; set; } = "Continue building Nekomata Core.";
    public List<string> Reasons { get; set; } = [];
}