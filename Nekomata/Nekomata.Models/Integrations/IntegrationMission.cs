namespace Nekomata.Models.Integrations;

public class IntegrationMission
{
    public string SourceType { get; set; } = "";

    public string SourceRecordId { get; set; } = "";

    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public string ExternalUrl { get; set; } = "";

    public string Customer { get; set; } = "";

    public string AssignedTo { get; set; } = "";

    public string Status { get; set; } = "";

    public string Priority { get; set; } = "";

    public DateTime? DueAt { get; set; }

    public DateTime? SlaExpiresAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public int ExternalStatusId { get; set; }

    public bool IsAwaitingExternalResponse { get; set; }

    public bool IsActionable { get; set; } = true;

    public decimal BusinessValue { get; set; }

    public int EstimatedMinutes { get; set; }

    public bool CustomerImpact { get; set; }

    public bool RevenueImpact { get; set; }

    public bool SecurityRelated { get; set; }

    public bool RequiresImmediateAttention { get; set; }

    public bool IsP1 { get; set; }

    public List<string> Tags { get; set; } = [];
}