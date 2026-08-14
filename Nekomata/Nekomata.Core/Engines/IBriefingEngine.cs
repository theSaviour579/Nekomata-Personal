using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public interface IBriefingEngine
{
    Task<NekomataWorkspace> GenerateAsync(
    NekomataWorkspace workspace);
}