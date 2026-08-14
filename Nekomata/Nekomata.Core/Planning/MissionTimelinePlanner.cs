using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public class MissionTimelinePlanner
{
    private readonly IEnumerable<ITimelineProvider>
        _providers;

    public MissionTimelinePlanner(
        IEnumerable<ITimelineProvider> providers)
    {
        _providers =
            providers;
    }

    public IReadOnlyList<MissionTimelineItem> Build(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var timeline =
            new List<MissionTimelineItem>();

        foreach (var provider in _providers)
        {
            timeline.AddRange(
                provider.Build(workspace));
        }

        return timeline
            .OrderBy(item => item.StartAt)
            .ToList();
    }
}