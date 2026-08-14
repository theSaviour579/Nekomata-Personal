using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public interface ICapacityEngine
{
    NekomataWorkspace Calculate(NekomataWorkspace workspace);
}