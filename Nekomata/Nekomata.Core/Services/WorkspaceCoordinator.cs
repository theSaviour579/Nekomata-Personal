using Nekomata.Core.Workspace;
using Nekomata.Models.Workspace;

public class WorkspaceCoordinator : IWorkspaceCoordinator
{
    private readonly IWorkspaceBuilder _builder;

    public WorkspaceCoordinator(IWorkspaceBuilder builder)
    {
        _builder = builder;
    }

    public NekomataWorkspace CurrentWorkspace { get; private set; }
        = new();

    public event Action<NekomataWorkspace>? WorkspaceChanged;

    public async Task<NekomataWorkspace> RefreshAsync()
    {
        CurrentWorkspace = await _builder.BuildAsync();

        WorkspaceChanged?.Invoke(CurrentWorkspace);

        System.Diagnostics.Debug.WriteLine("Workspace refreshed");

        return CurrentWorkspace;
    }
}