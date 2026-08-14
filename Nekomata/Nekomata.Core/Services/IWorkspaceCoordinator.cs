using Nekomata.Models.Workspace;

public interface IWorkspaceCoordinator
{
    Task<NekomataWorkspace> RefreshAsync();

    NekomataWorkspace CurrentWorkspace { get; }

    event Action<NekomataWorkspace>? WorkspaceChanged;
}