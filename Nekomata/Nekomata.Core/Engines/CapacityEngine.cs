using Nekomata.Models.Common;
using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public sealed class CapacityEngine : ICapacityEngine
{
    private readonly WorkingDaySettings _settings;
    private readonly DateTime _sessionStartedAt = DateTime.Now;

    public CapacityEngine(WorkingDaySettings settings)
    {
        _settings = settings;
    }

    public NekomataWorkspace Calculate(NekomataWorkspace workspace)
    {
        var now = DateTime.Now;
        var today = now.Date;
        var start = _settings.GetStart(today);
        var end = _settings.GetEnd(today);
        var totalWorkingMinutes = GetWorkingMinutes(start, end, today);
        var remainingWorkingMinutes = now >= end ? 0 : GetWorkingMinutes(now < start ? start : now, end, today);

        var committedTasks = workspace.Tasks
            .Where(task => !task.IsCompleted)
            .Where(task => task.DueAt.HasValue && task.DueAt.Value.Date <= today)
            .ToList();
        var taskMinutes = committedTasks.Sum(task => Math.Max(0, task.EstimatedMinutes - task.ActualMinutes));
        var missionAlreadyCounted = workspace.CurrentMission.TaskId.HasValue &&
            committedTasks.Any(task => task.Id == workspace.CurrentMission.TaskId.Value);
        var missionMinutes = missionAlreadyCounted ? 0 : Math.Max(0, (int)workspace.CurrentMission.EstimatedDuration.TotalMinutes);
        var urgentHaloMinutes = workspace.IntegrationMissionCandidates
            .Where(candidate => candidate.RequiresImmediateAttention)
            .Where(candidate => !string.Equals(candidate.Title, workspace.CurrentMission.Title, StringComparison.OrdinalIgnoreCase))
            .Sum(candidate => Math.Max(0, candidate.EstimatedMinutes));
        // Nekomata-managed calendar focus already occupies capacity. Remove that time
        // from the unscheduled work bucket so a planned task is not counted twice.
        var committedWorkMinutes = taskMinutes + missionMinutes + urgentHaloMinutes;
        var plannedMinutes = Math.Max(0, committedWorkMinutes - workspace.Capacity.ScheduledFocusMinutesToday);

        var capacity = workspace.Capacity;
        capacity.WorkdayStart = start;
        capacity.WorkdayEnd = end;
        capacity.WorkingMinutesToday = totalWorkingMinutes;
        var overtimeTrackingStart = _sessionStartedAt > end ? _sessionStartedAt : end;
        capacity.OvertimeMinutesWorked = now > overtimeTrackingStart
            ? Math.Max(0, (int)(now - overtimeTrackingStart).TotalMinutes)
            : 0;
        capacity.AvailableMinutesToday = Math.Max(0, remainingWorkingMinutes - capacity.ScheduledMinutesRemaining);
        capacity.PlannedMinutesToday = plannedMinutes;
        capacity.ExpectedFinishAt = now.AddMinutes(plannedMinutes + capacity.ScheduledMinutesRemaining);
        capacity.BurnoutRisk = GetBurnoutRisk(capacity.OvertimeMinutesWorked + capacity.ExpectedOvertimeMinutes);
        capacity.PushbackSuggestions = BuildPushbacks(workspace, capacity.ExpectedOvertimeMinutes);
        return workspace;
    }

    private int GetWorkingMinutes(DateTime from, DateTime to, DateTime date)
    {
        if (to <= from) return 0;
        var minutes = (int)(to - from).TotalMinutes;
        if (!_settings.IncludeLunchBreak) return minutes;
        var lunchStart = _settings.GetLunchStart(date);
        var lunchEnd = _settings.GetLunchEnd(date);
        var overlapStart = from > lunchStart ? from : lunchStart;
        var overlapEnd = to < lunchEnd ? to : lunchEnd;
        if (overlapEnd > overlapStart) minutes -= (int)(overlapEnd - overlapStart).TotalMinutes;
        return Math.Max(0, minutes);
    }

    private static string GetBurnoutRisk(int overtimeExposureMinutes) => overtimeExposureMinutes switch
    {
        >= 240 => "Critical",
        >= 120 => "High",
        >= 60 => "Moderate",
        _ => "Low"
    };

    private static List<CapacityPushbackSuggestion> BuildPushbacks(NekomataWorkspace workspace, int minutesToRecover)
    {
        if (minutesToRecover <= 0) return [];
        var suggestions = new List<CapacityPushbackSuggestion>();
        var recovered = 0;
        var candidates = workspace.RankedMissionCandidates
            .Where(candidate => !candidate.RequiresImmediateAttention)
            .Where(candidate => !candidate.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.DueAt?.Date <= DateTime.Today)
            .Where(candidate => candidate.TaskId != workspace.CurrentMission.TaskId || candidate.ProjectId != workspace.CurrentMission.ProjectId)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => PriorityRank(candidate.Priority))
            .ThenByDescending(candidate => candidate.DueAt)
            .ToList();

        foreach (var candidate in candidates)
        {
            var minutes = Math.Max(1, candidate.EstimatedMinutes);
            suggestions.Add(new CapacityPushbackSuggestion
            {
                Title = candidate.Title,
                Source = candidate.SourceType,
                Priority = candidate.Priority,
                Score = candidate.Score,
                MinutesRecovered = minutes,
                Reason = $"Lower-ranked {candidate.Priority.ToLowerInvariant()} work (score {candidate.Score}); moving it recovers {minutes} minutes."
            });
            recovered += minutes;
            if (recovered >= minutesToRecover) break;
        }
        return suggestions;
    }

    private static int PriorityRank(string priority) => priority.ToLowerInvariant() switch
    {
        "low" => 0,
        "normal" => 1,
        "high" => 2,
        _ => 3
    };
}