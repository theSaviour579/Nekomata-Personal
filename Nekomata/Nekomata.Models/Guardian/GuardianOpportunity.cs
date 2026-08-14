namespace Nekomata.Models.Guardian;

public class GuardianOpportunity
{
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public int EstimatedValue { get; set; }

    public int Priority { get; set; }

    public string Category { get; set; } = "";
}