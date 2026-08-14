namespace Nekomata.Models.Guardian;

public class GuardianReason
{
    public string Category { get; set; } = "";

    public string Title { get; set; } = "";

    public string Explanation { get; set; } = "";

    public int Weight { get; set; }

    public bool Positive { get; set; }
}