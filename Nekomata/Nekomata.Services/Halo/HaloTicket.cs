namespace Nekomata.Services.Halo;

public class HaloTicket
{
    public int Id { get; set; }

    public string Summary { get; set; } = "";

    public string Customer { get; set; } = "";

    public string Status { get; set; } = "";

    public string Priority { get; set; } = "";
    public string AgentName { get; set; } = "";

    public DateTime Created { get; set; }

    public DateTime? Due { get; set; }

    public DateTime? LastUpdatedAt { get; set; }

    public decimal BusinessValue { get; set; }

    public bool CustomerImpact { get; set; }

    public bool SecurityRelated { get; set; }

    public int EstimatedMinutes { get; set; }

    public int StatusId { get; set; }

    public int TicketTypeId { get; set; }

    public bool IsClosed { get; set; }

    public string HaloPriorityName { get; set; } = "";

    public bool IsPhishAlert { get; set; }

    public bool RequiresImmediateAttention { get; set; }

    public bool IsP1 { get; set; }
}