using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public interface ITimelineProvider
{
    string Name { get; }

    IReadOnlyList<MissionTimelineItem> Build(
        NekomataWorkspace workspace);
}