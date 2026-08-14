using Nekomata.Models.Planning;

namespace Nekomata.Core.Planning;

public class TimelineOptimiser
{
    public IReadOnlyList<MissionTimelineItem> Optimise(
        IEnumerable<MissionTimelineItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .OrderBy(item => item.StartAt)
            .ToList();
    }
}