using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Suggestions;

public class SuggestedMissionContext
{
    public NekomataWorkspace Workspace { get; }

    public DateTime Now { get; }

    public SuggestedMissionContext(
        NekomataWorkspace workspace,
        DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        Workspace =
            workspace;

        Now =
            now ?? DateTime.Now;
    }
}