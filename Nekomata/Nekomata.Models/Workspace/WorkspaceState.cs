namespace Nekomata.Models.Workspace;

public class WorkspaceState
{
    public bool OfflineMode { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}