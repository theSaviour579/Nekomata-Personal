namespace Nekomata.Models.Integrations;

public class IntegrationStatus
{
    public string Name { get; set; } = "";

    public bool Connected { get; set; }

    public DateTime? LastRefresh { get; set; }

    public int MissionCount { get; set; }

    public string Status { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public TimeSpan? RefreshDuration { get; set; }
}