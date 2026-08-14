using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public class FixedScheduleProvider
    : ITimelineProvider
{
    private readonly WorkingDaySettings
        _settings;

    public string Name =>
    "Working Day";

    public FixedScheduleProvider(
        WorkingDaySettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<MissionTimelineItem> Build(
    NekomataWorkspace workspace)
    {
        var items =
            new List<MissionTimelineItem>();

        if (_settings.IncludeLunchBreak)
        {
            items.Add(
                new MissionTimelineItem
                {
                    Title = "Lunch",

                    ItemType = "Break",

                    IsFixed = true,

                    StartAt =
                        _settings.GetLunchStart(DateTime.Now),

                    EndAt =
                        _settings.GetLunchEnd(DateTime.Now),

                    Status = "Scheduled"

                });
        }

        return items;
    }
}