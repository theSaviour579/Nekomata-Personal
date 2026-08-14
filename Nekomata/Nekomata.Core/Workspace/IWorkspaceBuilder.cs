using Nekomata.Models.Workspace;

namespace Nekomata.Core.Workspace;

public interface IWorkspaceBuilder
{
    Task<NekomataWorkspace> BuildAsync();
}