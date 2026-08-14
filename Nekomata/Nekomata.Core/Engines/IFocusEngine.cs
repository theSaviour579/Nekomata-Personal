using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public interface IFocusEngine
{
    NekomataWorkspace BuildFocus(NekomataWorkspace workspace);
}