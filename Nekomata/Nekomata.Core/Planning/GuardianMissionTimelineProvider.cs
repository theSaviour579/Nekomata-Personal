using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Planning;

public class GuardianMissionTimelineProvider
    : ITimelineProvider
{
    private readonly WorkingDaySettings
        _settings;

    public string Name =>
        "Guardian Mission";

    public GuardianMissionTimelineProvider(
        WorkingDaySettings settings)
    {
        _settings =
            settings;
    }

    public IReadOnlyList<MissionTimelineItem> Build(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var items =
            new List<MissionTimelineItem>();

        if (workspace.CurrentMission is null)
        {
            return items;
        }

        var current =
            DateTime.Now;

        var requestedEnd =
            current.Add(
                workspace.CurrentMission
                    .EstimatedDuration);

        var workdayEnd =
            _settings.GetEnd(current);

        var scheduledEnd =
            requestedEnd <= workdayEnd
                ? requestedEnd
                : workdayEnd;

        if (scheduledEnd <= current)
        {
            return items;
        }

        items.Add(
            new MissionTimelineItem
            {
                Title =
                    workspace.CurrentMission.Title,

                ItemType =
                    "Mission",

                SourceType =
                    workspace.CurrentMission.SourceType,

                SourceRecordId =
                    workspace.CurrentMission.TaskId?
                        .ToString(),

                StartAt =
                    current,

                EndAt =
                    scheduledEnd,

                RemainingMinutes =
                    Math.Max(
                        (int)(requestedEnd - scheduledEnd)
                            .TotalMinutes,
                        0),

                Score =
                    workspace.CurrentMission.Score,

                BusinessValue =
                    workspace.CurrentMission.BusinessValue,

                Description =
                    workspace.CurrentMission
                        .RecommendationReason,

                Status =
                    requestedEnd > workdayEnd
                        ? "Partially Scheduled"
                        : "Planned",

                IsFixed = false
            });

        return items;
    }
}