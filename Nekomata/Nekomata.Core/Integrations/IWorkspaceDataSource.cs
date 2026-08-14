using Nekomata.Core.Integrations;

public interface IWorkspaceDataSource
{
    string Name { get; }

    Task<WorkspaceDataSnapshot> LoadAsync(
        CancellationToken cancellationToken = default);
}