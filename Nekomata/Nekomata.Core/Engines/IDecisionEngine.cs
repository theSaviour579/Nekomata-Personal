using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public interface IDecisionEngine
{
    NekomataWorkspace Analyse(NekomataWorkspace workspace);
}