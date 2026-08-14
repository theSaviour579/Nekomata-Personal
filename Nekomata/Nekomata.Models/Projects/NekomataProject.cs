namespace Nekomata.Models.Projects;

public class NekomataProject
{
    public long Id { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public string Status { get; set; } = "Active";
    public string Priority { get; set; } = "Normal";

    public int ProgressPercent { get; set; }

    public decimal EstimatedBusinessValue { get; set; }

    public int EstimatedRemainingMinutes { get; set; }

    public DateTime? DueAt { get; set; }

    public bool AtRisk { get; set; }

    public string? NextAction { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}