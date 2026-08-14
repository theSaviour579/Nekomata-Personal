namespace Nekomata.AI.Models.Actions;

public class ProposedTask
{
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string Priority { get; set; } = "Normal";

    public int EstimatedMinutes { get; set; }

    public decimal EstimatedBusinessValue { get; set; }

    public bool RequiresSql { get; set; }

    public bool RequiresFocus { get; set; }

    public string? SuggestedDelegate { get; set; }

    public bool Selected { get; set; } = true;
}