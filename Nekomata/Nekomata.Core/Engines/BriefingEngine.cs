using Nekomata.Core.Analytics.Capacity;
using Nekomata.Core.Analytics.Models;
using Nekomata.Core.Engines.Guardian;
using Nekomata.Core.Guardian.Decisions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Briefing;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Nekomata.Core.Personalization;
namespace Nekomata.Core.Engines;

public class BriefingEngine : IBriefingEngine
{
    private readonly IMissionSessionRepository
        _missionSessionRepository;

    private readonly IGuardianDecisionEngine _decisionEngine;

    private readonly IDailyCapacityCalculator
    _capacityCalculator;

    private readonly IUserIdentity _userIdentity;

    public BriefingEngine(
        IMissionSessionRepository missionSessionRepository,
        IGuardianDecisionEngine decisionEngine,
        IDailyCapacityCalculator capacityCalculator,
        IUserIdentity userIdentity)
    {
        _missionSessionRepository = missionSessionRepository;
        _decisionEngine = decisionEngine;
        _capacityCalculator = capacityCalculator;
        _userIdentity = userIdentity;
    }

    public async Task<NekomataWorkspace> GenerateAsync(
     NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var now = DateTime.Now;
        var today = now.Date;
        var yesterday = today.AddDays(-1);

        var yesterdaySessions =
            await _missionSessionRepository.GetBetweenAsync(
                yesterday,
                today);

        var openTasks =
            workspace.Tasks
                .Where(IsOpen)
                .ToList();

        var dueToday =
            openTasks
                .Where(task =>
                    task.DueAt is not null &&
                    task.DueAt.Value.Date == today)
                .OrderByDescending(task => task.PriorityScore)
                .ThenBy(task => task.DueAt)
                .ToList();

        var overdue =
            openTasks
                .Where(task =>
                    task.DueAt is not null &&
                    task.DueAt.Value < now)
                .OrderBy(task => task.DueAt)
                .ToList();

        var unscheduled =
            openTasks
                .Where(task => task.DueAt is null)
                .ToList();

        var critical =
            openTasks
                .Where(task =>
                    string.Equals(
                        task.Priority,
                        "Critical",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        PopulateYesterday(
            workspace,
            yesterdaySessions);

        PopulateToday(
            workspace,
            dueToday,
            overdue,
            unscheduled,
            critical,
            workspace.IntegrationMissionCandidates.Count(candidate => candidate.IsP1));

        PopulateObjective(workspace);

        System.Diagnostics.Debug.WriteLine(
    $"Before Capacity: {workspace.CurrentMission?.Title}");

        System.Diagnostics.Debug.WriteLine(
            $"Estimated Minutes: {workspace.CurrentMission?.EstimatedDuration.TotalMinutes}");

        PopulateCapacity(workspace);

        var decision =
            _decisionEngine.Analyse(workspace);

        PopulateGuidance(
            workspace,
            decision);

        return workspace;
    }

    private static bool IsOpen(
        NekomataTask task)
    {
        return !string.Equals(
            task.Status,
            "Completed",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void PopulateYesterday(
        NekomataWorkspace workspace,
        IReadOnlyCollection<MissionSession> sessions)
    {
        var completed =
            sessions
                .Where(session => session.Completed)
                .ToList();

        workspace.Briefing
            .MissionsCompletedYesterday =
                completed.Count;

        workspace.Briefing
            .MissionsCancelledYesterday =
                sessions.Count(session =>
                    session.Cancelled);

        workspace.Briefing
            .FocusMinutesYesterday =
                completed.Sum(session =>
                    session.ActualDurationMinutes);

        workspace.Briefing
            .BusinessValueYesterday =
                completed.Sum(session =>
                    session.BusinessValue);

        workspace.Briefing
            .AverageScoreYesterday =
                completed.Count == 0
                    ? 0
                    : completed.Average(session =>
                        session.Score);

        workspace.Briefing.YesterdaySummary =
            completed.Count switch
            {
                0 =>
                    "No completed missions were recorded yesterday.",

                1 =>
                    "One mission was completed yesterday.",

                _ =>
                    $"{completed.Count} missions were completed yesterday."
            };
    }

    private static void PopulateToday(
        NekomataWorkspace workspace,
        IReadOnlyCollection<NekomataTask> dueToday,
        IReadOnlyCollection<NekomataTask> overdue,
        IReadOnlyCollection<NekomataTask> unscheduled,
        IReadOnlyCollection<NekomataTask> critical,
        int assignedP1Count)
    {
        workspace.Briefing.TasksDueToday =
            dueToday.Count;

        workspace.Briefing.OverdueTasks =
            overdue.Count;

        workspace.Briefing.UnscheduledTasks =
            unscheduled.Count;

        workspace.Briefing.CriticalTasks =
            critical.Count + assignedP1Count;

        workspace.Briefing.BusinessValueToday =
            dueToday.Sum(task =>
                task.EstimatedBusinessValue);

        workspace.Briefing.TodaySummary =
            BuildTodaySummary(
                dueToday.Count,
                overdue.Count,
                critical.Count + assignedP1Count);
    }

    private static void PopulateObjective(
        NekomataWorkspace workspace)
    {
        var mission =
            workspace.CurrentMission;

        System.Diagnostics.Debug.WriteLine(
    $"Mission = {mission?.Title ?? "NULL"}");

        if (mission is null)
        {
            workspace.Briefing.PrimaryFocus =
                workspace.Focus.PrimaryFocus
                ?? "No primary objective selected.";

            workspace.Briefing.ObjectiveTitle =
                "No primary objective";

            workspace.Briefing.ObjectiveReason =
                "Guardian could not identify a mission that currently justifies prioritisation.";

            workspace.Briefing.ObjectiveScore = 0;
            workspace.Briefing.ObjectiveBusinessValue = 0;
            workspace.Briefing.ObjectiveEstimatedMinutes = 0;
            workspace.Briefing.ObjectiveStartBefore = null;
            workspace.Briefing.ObjectiveTaskId = null;
            workspace.Briefing.ObjectiveProjectId = null;

            return;
        }

        workspace.Briefing.ObjectiveTitle =
            mission.Title;

        workspace.Briefing.PrimaryFocus =
            mission.Title;

        workspace.Briefing.ObjectiveReason =
            mission.RecommendationReason;

        workspace.Briefing.ObjectiveScore =
            mission.Score;

        workspace.Briefing.ObjectiveBusinessValue =
            mission.BusinessValue;

        workspace.Briefing.ObjectiveEstimatedMinutes =
            (int)mission.EstimatedDuration.TotalMinutes;

        workspace.Briefing.ObjectiveStartBefore =
            mission.StartBefore;

        workspace.Briefing.ObjectiveTaskId =
            mission.TaskId;

        workspace.Briefing.ObjectiveProjectId =
            mission.ProjectId;
    }

    private void PopulateCapacity(
        NekomataWorkspace workspace)
    {
        var capacity = _capacityCalculator.Calculate(workspace);

        System.Diagnostics.Debug.WriteLine(
            $"Capacity %: {capacity.UtilisationPercent}");

        System.Diagnostics.Debug.WriteLine(
            $"Capacity Planned: {capacity.PlannedMinutes}");

        workspace.Briefing.AvailableMinutesToday =
            capacity.AvailableMinutes;

        workspace.Briefing.PlannedMinutesToday =
            capacity.PlannedMinutes;

        workspace.Briefing.RemainingCapacityMinutes =
            capacity.RemainingMinutes;

        workspace.Briefing.CapacityUsedPercent =
            capacity.UtilisationPercent;

        System.Diagnostics.Debug.WriteLine(
    $"Briefing Capacity = {workspace.Briefing.CapacityUsedPercent}");

        workspace.Briefing.IsOverCapacity =
            capacity.IsOverCapacity;

        workspace.Briefing.OvertimeMinutesWorked = workspace.Capacity.OvertimeMinutesWorked;
        workspace.Briefing.ExpectedOvertimeMinutes = workspace.Capacity.ExpectedOvertimeMinutes;
        workspace.Briefing.BurnoutRisk = workspace.Capacity.BurnoutRisk;
        workspace.Briefing.ExpectedFinishAt = workspace.Capacity.ExpectedFinishAt;

        workspace.Briefing.CapacitySummary =
            BuildCapacitySummary(capacity);

        System.Diagnostics.Debug.WriteLine(
    $"BRIEFING OVER CAPACITY: {workspace.Briefing.IsOverCapacity}");

        System.Diagnostics.Debug.WriteLine(
            $"BRIEFING REMAINING: {workspace.Briefing.RemainingCapacityMinutes}");

        System.Diagnostics.Debug.WriteLine(
            $"BRIEFING SUMMARY: {workspace.Briefing.CapacitySummary}");
    }

    private void PopulateGuidance(
     NekomataWorkspace workspace,
     GuardianDecision decision)
    {
        var briefing = workspace.Briefing;
        
        briefing.Greeting =
            BuildGreeting(DateTime.Now, _userIdentity.DisplayName);

        briefing.Headline =
            decision.Headline;

        briefing.GuardianComment =
            decision.Recommendation;

        briefing.GuardianConfidence =
            decision.Confidence;

        briefing.AiSummary =
            BuildAiSummary(briefing);

        briefing.GuardianReasons.Clear();
        briefing.GuardianRisks.Clear();
        briefing.Opportunities.Clear();

        foreach (var reason in decision.Reasons)
            briefing.GuardianReasons.Add(reason);

        foreach (var risk in decision.Risks)
            briefing.GuardianRisks.Add(risk);

        System.Diagnostics.Debug.WriteLine(
    $"Briefing Risks = {briefing.GuardianRisks.Count}");

        foreach (var risk in briefing.GuardianRisks)
        {
            System.Diagnostics.Debug.WriteLine(
                $"BRIEFING RISK: {risk.Title}");
        }

        foreach (var opportunity in decision.Opportunities)
            briefing.Opportunities.Add(opportunity);

        //-------------------------------------------------------
        // Workspace Health
        //-------------------------------------------------------

        briefing.WorkspaceHealthScore =
            workspace.GuardianEvidence.WorkspaceHealthScore;

        briefing.HealthWarnings.Clear();

        foreach (var warning in
                 workspace.GuardianEvidence.HealthWarnings)
        {
            briefing.HealthWarnings.Add(
                warning);
        }
    }

    private static string BuildGreeting(
        DateTime now,
        string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName.Trim();
        return now.Hour switch
        {
            < 12 => $"Good morning, {name}.",
            < 17 => $"Good afternoon, {name}.",
            _ => $"Good evening, {name}."
        };
    }

    private static string BuildTodaySummary(
        int dueToday,
        int overdue,
        int critical)
    {
        return
            $"{dueToday} due today, " +
            $"{overdue} overdue and " +
            $"{critical} critical.";
    }

    private static string BuildAiSummary(
        Models.Briefing.MorningBriefing briefing)
    {
        var valueText =
            briefing.BusinessValueToday > 0
                ? $" Today’s due work carries an estimated value of {briefing.BusinessValueToday:C0}."
                : "";

        var objectiveText =
            !string.IsNullOrWhiteSpace(
                briefing.ObjectiveTitle)
                ? $" Guardian recommends starting with {briefing.ObjectiveTitle}."
                : "";

        return
            $"{briefing.TodaySummary}{valueText}{objectiveText}";
    }

    private static string FormatDuration(
        int totalMinutes)
    {
        var hours =
            totalMinutes / 60;

        var minutes =
            totalMinutes % 60;

        if (hours == 0)
            return $"{minutes} minutes";

        if (minutes == 0)
            return hours == 1
                ? "1 hour"
                : $"{hours} hours";

        return $"{hours}h {minutes}m";
    }
    private static string BuildCapacitySummary(
    DailyCapacity capacity)
    {
        if (capacity.IsOverCapacity)
        {
            return
                $"Today's workload exceeds capacity by {FormatDuration(capacity.OverCapacityMinutes)}.";
        }

        if (capacity.RemainingMinutes >= 180)
        {
            return
                $"Approximately {FormatDuration(capacity.RemainingMinutes)} remain available today.";
        }

        if (capacity.RemainingMinutes >= 60)
        {
            return
                $"Today's workload is healthy with {FormatDuration(capacity.RemainingMinutes)} remaining.";
        }

        if (capacity.RemainingMinutes > 0)
        {
            return
                $"Today's schedule is nearly full ({FormatDuration(capacity.RemainingMinutes)} remaining).";
        }

        return
            "Today's schedule is fully allocated.";
    }
}
