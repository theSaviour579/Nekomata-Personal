namespace Nekomata.Models.Guardian;

public class GuardianMemory
{
    public long Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public string Category { get; set; } = "";

    public int Importance { get; set; } = 50;

    public string Source { get; set; } = "";

    public string Summary { get; set; } = "";

    public string? Detail { get; set; }

    public long? ProjectId { get; set; }

    public long? TaskId { get; set; }

    public string? Metadata { get; set; }
}