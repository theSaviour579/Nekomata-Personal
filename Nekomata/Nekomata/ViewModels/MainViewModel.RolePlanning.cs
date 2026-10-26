using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Analytics;
using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Guardian.Commands;
using Nekomata.Core.Missions;
using Nekomata.Core.Planning;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private RolePlanningPlan? rolePlanning;
    [ObservableProperty] private RoleAlignmentForecast? alignmentForecast;
    private bool _rolePlanningRefreshBusy;

    private async Task RefreshRolePlanningAsync()
    {
        if (_rolePlanningRefreshBusy) return;
        _rolePlanningRefreshBusy = true;
        try
        {
            var now = DateTime.Now;
            var monday = StartOfWeek(now);
            var weekEnd = monday.AddDays(5).AddTicks(-1);
            var alignmentEngine = _services.GetRequiredService<RoleAlignmentEngine>();
            var alignment = await alignmentEngine.BuildCurrentWeekAsync(now);
            var history = await alignmentEngine.BuildCompletedWeekHistoryAsync(now, 6);
            var value = await _services.GetRequiredService<ExecutiveValueEngine>().BuildAsync(monday, now);
            var journal = await _services.GetRequiredService<IWorkdayEventRepository>()
                .GetBetweenAsync(new DateTimeOffset(monday), new DateTimeOffset(now.AddMinutes(1)), 2000);
            var interruptions = (await _services.GetRequiredService<IGuardianInterruptionRepository>().GetAllAsync())
                .Where(item => item.InterruptedAt >= new DateTimeOffset(monday.AddDays(-42))).ToList();
            var causeWeights = BuildCauseWeights(value.Episodes, journal);

            var historyStore = _services.GetRequiredService<IRoleBiasHistoryStore>();
            var storedBiases = await historyStore.LoadAsync();
            var biases = _services.GetRequiredService<RoleBiasEngine>()
                .Calculate(alignment, history, causeWeights, storedBiases, monday);
            await historyStore.UpsertAsync(RoleBiasEngine.UpdateHistory(storedBiases, biases, monday));

            var futureCalendar = await GetRolePlanningCalendarAsync(now, weekEnd);
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var capacity = RemainingWeekCapacity(now, weekEnd, futureCalendar, settings);
            var protectedMinutes = futureCalendar.Where(item =>
                    RoleDriftCauseClassifier.ClassifyBand(item.Subject, "Calendar") is "strategic" or "leadership")
                .Sum(item => Math.Max(0, (int)(item.End - item.Start).TotalMinutes));
            var reactive = alignment.Bands.FirstOrDefault(item => item.Key == "reactive")?.ActualPercent ?? 0;
            var bestBlock = _services.GetRequiredService<InterruptionRiskEstimator>().SelectBest(
                BuildProtectedBlockCandidates(now, weekEnd, futureCalendar, settings), interruptions, journal, reactive);
            var overtime = await _services.GetRequiredService<WeeklyOvertimeService>().GetCurrentWeekAsync(now);
            var evaluator = _services.GetRequiredService<CorrectionOutcomeEvaluator>();
            await evaluator.EvaluateAsync(alignment.Score, journal);
            var recentCorrections = await evaluator.GetRecentAsync(500);
            var effectiveness = recentCorrections
                .Where(item => item.ObservedAt is not null && !item.ExternalDisruptionPrevented)
                .GroupBy(item => item.Type)
                .ToDictionary(group => group.Key, group => group.Average(item => item.Occurred ? 1d : 0d));
            var plan = _services.GetRequiredService<CorrectivePlanEngine>().Build(
                alignment, biases, capacity, overtime.TotalMinutes, protectedMinutes, bestBlock,
                bestBlock?.RiskPercent ?? 50, effectiveness);

            var trackedActions = new List<RoleCorrectiveAction>();
            foreach (var action in plan.RankedActions)
            {
                var id = await evaluator.RecordRecommendationAsync(action, alignment.Score, "Role Alignment corrective-plan arbitration");
                var tracked = (await evaluator.GetRecentAsync()).FirstOrDefault(item => item.Id == id);
                trackedActions.Add(action with { Id = id, Decision = tracked?.Decision ?? RoleCorrectionDecision.Proposed });
            }
            plan = plan with { RankedActions = trackedActions };

            var forecast = _services.GetRequiredService<RoleAlignmentForecastEngine>().Calculate(
                alignment, BuildProjectedActivities(futureCalendar, capacity),
                BuildProjectedCorrections(plan, await evaluator.GetRecentAsync()),
                _services.GetRequiredService<RoleAlignmentSettings>());
            RoleAlignment = alignment;
            AlignmentForecast = forecast;
            RolePlanning = _services.GetRequiredService<RolePlanningPolicy>()
                .Create(alignment, now, biases, plan, forecast);
            ApplyRolePlanningBias(Workspace, replaceCurrentMission: !MissionActive && !CalendarObjectiveAvailable);
            OnPropertyChanged(nameof(Workspace));
        }
        finally { _rolePlanningRefreshBusy = false; }
    }

    private async Task<IReadOnlyList<CalendarEvent>> GetRolePlanningCalendarAsync(DateTime now, DateTime weekEnd)
    {
        if (weekEnd <= now) return [];
        try
        {
            return await _services.GetRequiredService<ICalendarService>()
                .GetEventsAsync(new DateTimeOffset(now), new DateTimeOffset(weekEnd));
        }
        catch
        {
            return CalendarEvents.Where(item => item.End > new DateTimeOffset(now) && item.Start < new DateTimeOffset(weekEnd)).ToList();
        }
    }

    private IReadOnlyList<ProjectedRoleActivity> BuildProjectedActivities(IReadOnlyList<CalendarEvent> calendar, int capacity)
    {
        var result = calendar.Where(item => !item.IsAllDay).Select(item => new ProjectedRoleActivity(
            RoleDriftCauseClassifier.ClassifyBand(item.Subject, "Calendar"),
            Math.Max(0, (int)(item.End - item.Start).TotalMinutes), $"Calendar: {item.Subject}", ProjectedActivityDisposition.Planned)).ToList();
        var remaining = Math.Max(0, capacity);
        foreach (var mission in Workspace.RankedMissionCandidates.Where(item => item.IsActionable && !item.IsOnHold))
        {
            var minutes = Math.Min(remaining, Math.Max(5, mission.PredictedActiveMinutes > 0 ? mission.PredictedActiveMinutes : mission.EstimatedMinutes));
            if (minutes <= 0) break;
            result.Add(new(RoleDriftCauseClassifier.ClassifyBand(mission.Title, mission.SourceType), minutes,
                $"Planned mission: {mission.Title}", ProjectedActivityDisposition.Planned));
            remaining -= minutes;
        }
        return result;
    }

    private static IReadOnlyList<ProjectedRoleActivity> BuildProjectedCorrections(
        RoleCorrectivePlan plan, IReadOnlyList<RoleCorrectionOutcome> tracked)
    {
        var currentIds = plan.RankedActions.Select(item => item.Id).ToHashSet();
        var activities = plan.RankedActions.Select(ToProjectedCorrection).ToList();
        activities.AddRange(tracked.Where(item => !currentIds.Contains(item.Id) && item.Decision == RoleCorrectionDecision.Approved &&
                                             item.RecommendedAt >= DateTimeOffset.Now.AddDays(-7))
            .Select(item => new ProjectedRoleActivity(item.BandKey, MinutesFor(item.Type), $"Approved correction: {item.Recommendation}",
                ProjectedActivityDisposition.Accepted, item.Type is RoleCorrectionType.DelegateRecurringWork or RoleCorrectionType.ReduceAdministration)));
        return activities;

        static ProjectedRoleActivity ToProjectedCorrection(RoleCorrectiveAction action) => new(
            action.BandKey, action.Minutes, $"Corrective plan: {action.Title}", action.Decision switch
            {
                RoleCorrectionDecision.Approved => ProjectedActivityDisposition.Accepted,
                RoleCorrectionDecision.Rejected => ProjectedActivityDisposition.Rejected,
                _ => ProjectedActivityDisposition.Proposed
            }, action.Type is RoleCorrectionType.DelegateRecurringWork or RoleCorrectionType.ReduceAdministration);
        static int MinutesFor(RoleCorrectionType type) => type switch
        {
            RoleCorrectionType.ReserveStrategicBlock => 90,
            RoleCorrectionType.PreserveLeadershipCommitment => 60,
            RoleCorrectionType.ProtectOversightBlock => 45,
            _ => 30
        };
    }

    private IReadOnlyDictionary<string, double> BuildCauseWeights(
        IReadOnlyList<WorkEpisode> episodes, IReadOnlyCollection<WorkdayEvent> journal)
    {
        var classifier = _services.GetRequiredService<RoleDriftCauseClassifier>();
        var result = new Dictionary<string, double>();
        foreach (var group in episodes.GroupBy(RoleAlignmentEngine.RoleBandKey))
        {
            var weighted = group.Select(item => (item.ActiveMinutes, Evidence: classifier.Classify(item, journal))).ToList();
            result[group.Key] = weighted.Sum(item => item.ActiveMinutes) == 0 ? 1 :
                weighted.Sum(item => item.ActiveMinutes * item.Evidence.CorrectionWeight) / weighted.Sum(item => item.ActiveMinutes);
        }
        var unavoidableMinutes = episodes.Where(item => classifier.Classify(item, journal).Cause == RoleDriftCause.UnavoidableIncident)
            .Sum(item => item.ActiveMinutes);
        var totalMinutes = episodes.Sum(item => item.ActiveMinutes);
        var globalDamping = totalMinutes == 0 ? 1 : Math.Clamp(1 - .8 * unavoidableMinutes / totalMinutes, .3, 1);
        foreach (var key in new[] { "strategic", "leadership", "oversight", "technical", "reactive", "admin" })
            result[key] = Math.Min(result.GetValueOrDefault(key, 1), globalDamping);
        if (journal.Any(item => ($"{item.EventType} {item.Narrative}").Contains("cancel", StringComparison.OrdinalIgnoreCase) &&
                                ($"{item.EventType} {item.Narrative}").Contains("meeting", StringComparison.OrdinalIgnoreCase)))
            result["leadership"] = Math.Min(result["leadership"], .35);
        return result;
    }

    private static IReadOnlyList<ProtectedBlockCandidate> BuildProtectedBlockCandidates(
        DateTime now, DateTime weekEnd, IReadOnlyList<CalendarEvent> calendar, WorkingDaySettings settings)
    {
        var candidates = new List<ProtectedBlockCandidate>();
        for (var day = now.Date; day <= weekEnd.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var first = day == now.Date && now > settings.GetStart(day) ? now.AddMinutes(29 - (now.Minute + 29) % 30) : settings.GetStart(day);
            for (var start = first; start.AddMinutes(90) <= settings.GetEnd(day); start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(90);
                if (settings.IncludeLunchBreak && start < settings.GetLunchEnd(day) && end > settings.GetLunchStart(day)) continue;
                if (calendar.Any(item => !item.IsAllDay && item.Start < new DateTimeOffset(end) && item.End > new DateTimeOffset(start))) continue;
                var density = calendar.Count(item => !item.IsAllDay && item.Start < new DateTimeOffset(end.AddHours(2)) && item.End > new DateTimeOffset(start.AddHours(-2)));
                candidates.Add(new(new DateTimeOffset(start), new DateTimeOffset(end), density));
            }
        }
        return candidates;
    }

    private static int RemainingWeekCapacity(DateTime now, DateTime weekEnd, IReadOnlyList<CalendarEvent> calendar, WorkingDaySettings settings)
    {
        var total = 0;
        for (var day = now.Date; day <= weekEnd.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var start = day == now.Date && now > settings.GetStart(day) ? now : settings.GetStart(day);
            var end = settings.GetEnd(day);
            if (start >= end) continue;
            total += (int)(end - start).TotalMinutes;
            if (settings.IncludeLunchBreak)
            {
                var overlapStart = start > settings.GetLunchStart(day) ? start : settings.GetLunchStart(day);
                var overlapEnd = end < settings.GetLunchEnd(day) ? end : settings.GetLunchEnd(day);
                if (overlapEnd > overlapStart) total -= (int)(overlapEnd - overlapStart).TotalMinutes;
            }
        }
        total -= calendar.Where(item => !item.IsAllDay).Sum(item => Math.Max(0, (int)(item.End - item.Start).TotalMinutes));
        return Math.Max(0, total);
    }

    private static DateTime StartOfWeek(DateTime value) => value.Date.AddDays(-((7 + (int)value.DayOfWeek - (int)DayOfWeek.Monday) % 7));

    private async Task ShowRolePlanningCommandAsync(GuardianCommandIntent intent)
    {
        await RefreshRolePlanningAsync();
        ShowPaletteResponse(FormatRolePlanningCommand(intent));
    }

    private string FormatRolePlanningCommand(GuardianCommandIntent intent)
    {
        var forecast = AlignmentForecast;
        var plan = RolePlanning?.CorrectivePlan;
        var biases = RolePlanning?.ActiveBiases ?? [];
        if (forecast is null || RoleAlignment?.HasEvidence != true)
            return "Guardian does not yet have enough completed WorkEpisode evidence to produce a defensible role-alignment forecast.";
        return intent switch
        {
            GuardianCommandIntent.ForecastRoleAlignment =>
                $"Role alignment forecast · estimates\n• Current observed: {forecast.Current.Score}%\n" +
                $"• End of week with no intervention · ESTIMATE: {forecast.NoIntervention.Score}%\n" +
                $"• If Guardian's recommended plan is followed · ESTIMATE: {forecast.RecommendedPlan.Score}%\n" +
                $"• Expected difference · ESTIMATE: {forecast.ExpectedImprovement:+0.#;-0.#;0} points\n\n" +
                string.Join("\n", forecast.Evidence.Select(item => "Evidence: " + item)),
            GuardianCommandIntent.ImproveRoleAlignment => FormatCorrectivePlan(plan),
            GuardianCommandIntent.ExplainRoleDrift =>
                "What appears to be pulling you out of role\n" +
                string.Join("\n", RoleAlignment.Bands.Where(item => item.Status != "Within range")
                    .OrderByDescending(item => Math.Abs(item.ActualPercent - (item.MinimumPercent + item.MaximumPercent) / 2))
                    .Select(item => $"• {item.Name}: {item.ActualPercent:0.#}% against {item.TargetLabel}. {item.Guidance}")) +
                (biases.Count == 0 ? "" : "\n\nCause-aware planning pressure:\n" + string.Join("\n", biases.Select(item => $"• {item.BandName}: {item.MainReason}"))) +
                "\n\nThese are evidence-based associations; Guardian does not claim that any single event caused the drift.",
            GuardianCommandIntent.ShowMostImportantCorrection => plan?.MostImportant is null
                ? "No corrective action is justified by the current evidence and available capacity."
                : $"Most important corrective action · ESTIMATE\n{plan.MostImportant.Title}\n" +
                  $"Priority {plan.MostImportant.PriorityScore}/100 · {plan.MostImportant.EstimateLabel}\n" +
                  $"Why: {plan.MostImportant.Reason}\nEvidence: {string.Join("; ", plan.MostImportant.Evidence)}",
            GuardianCommandIntent.ProtectStrategicTime => FormatStrategicProtection(plan),
            GuardianCommandIntent.ExplainRoleBias => biases.Count == 0
                ? "No active role-alignment planning bias is justified by the current evidence."
                : "Active planning biases\n" + string.Join("\n\n", biases.Select(item =>
                    $"• {item.BandName} · {item.StrengthLabel} {item.Strength}/100 · {item.Direction}\n  {item.MainReason}\n  Evidence: {string.Join("; ", item.Evidence)}")),
            _ => FormatCorrectivePlan(plan)
        };
    }

    private static string FormatCorrectivePlan(RoleCorrectivePlan? plan)
    {
        if (plan is null || plan.RankedActions.Count == 0)
            return "Guardian found no minimum viable role correction justified by the current evidence and available capacity.";
        return "Guardian's minimum viable corrective plan · estimates\n" +
               string.Join("\n", plan.RankedActions.Select((item, index) =>
                   $"{index + 1}. {item.Title} · priority {item.PriorityScore}/100 · {item.EstimateLabel}" +
                   (item.SuggestedBlock is null ? "" : $" · suggested {item.SuggestedBlock.Start.LocalDateTime:ddd HH:mm} ({item.SuggestedBlock.RiskPercent}% interruption risk)"))) +
               $"\n\nExpected plan improvement · ESTIMATE: {plan.ExpectedAlignmentImprovement:+0.#;-0.#;0} points. {plan.Rationale}\n" +
               "Calendar changes and delegation still require explicit approval; nothing has been applied.";
    }

    private static string FormatStrategicProtection(RoleCorrectivePlan? plan)
    {
        var action = plan?.RankedActions.FirstOrDefault(item => item.Type == RoleCorrectionType.ReserveStrategicBlock);
        if (action is null) return "Guardian does not currently have evidence for a strategic-time correction.";
        var slot = action.SuggestedBlock is null ? "No defensible free slot was found." :
            $"Best estimated slot: {action.SuggestedBlock.Start.LocalDateTime:dddd HH:mm}–{action.SuggestedBlock.End.LocalDateTime:HH:mm} · {action.SuggestedBlock.RiskPercent}% estimated interruption risk.";
        return $"Strategic-time protection proposal · ESTIMATE\n{slot}\n{action.Reason}\n\nThis is prepared advice only. Open pre-flight or Calendar to review it; no calendar event has been created.";
    }

    private void ApplyRolePlanningBias(Nekomata.Models.Workspace.NekomataWorkspace workspace, bool replaceCurrentMission)
    {
        if (RolePlanning is null || workspace.RankedMissionCandidates.Count == 0) return;
        workspace.RankedMissionCandidates = _services.GetRequiredService<RolePlanningPolicy>()
            .Apply(RolePlanning, workspace.RankedMissionCandidates).ToList();
        if (!replaceCurrentMission || workspace.RankedMissionCandidates.Count == 0) return;
        workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(workspace.RankedMissionCandidates[0]);
        workspace.CurrentMission.Decision = _services.GetRequiredService<GuardianMissionDecisionBuilder>()
            .Build(workspace.CurrentMission, workspace.RankedMissionCandidates, workspace.Guardian.Confidence);
    }
}
