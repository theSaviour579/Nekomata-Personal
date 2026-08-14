using Nekomata.Core.Integrations;
using Nekomata.Models.Integrations;
using Nekomata.Models.Missions;

public class WorkspaceDataSnapshot
{
    public string SourceName { get; set; } = "";

    public DateTime RetrievedAt { get; set; }

    public List<IntegrationMission> IntegrationMissions { get; }
        = [];

    public List<string> Notifications { get; }
        = [];

    public IntegrationHealth Health { get; set; }
        = new();

    public List<IntegrationStatus> Integrations { get; }
    = [];
}