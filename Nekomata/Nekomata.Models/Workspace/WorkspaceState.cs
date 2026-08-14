namespace Nekomata.Models.Workspace;

public class WorkspaceState
{
    public bool HaloConnected { get; set; }

    public bool OutlookConnected { get; set; }

    public bool GrafanaConnected { get; set; }

    public bool MerlinConnected { get; set; }

    public bool OfflineMode { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}