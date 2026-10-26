using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Simulation;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Models.Common;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;
using Nekomata.UI.Windows;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private DateTimeOffset _decisionSimulationCalendarRangeStart;
    private DateTimeOffset _decisionSimulationCalendarRangeEnd;
    [ObservableProperty] private ObservableCollection<GuardianDayScenario> decisionScenarios = [];
    [ObservableProperty] private GuardianDayScenario? selectedDecisionScenario;
    [ObservableProperty] private ObservableCollection<MissionCandidate> decisionSimulationCandidates = [];
    [ObservableProperty] private MissionCandidate? selectedDecisionCandidate;
    [ObservableProperty] private string decisionSimulationLeaveTime = "16:00";
    [ObservableProperty] private int decisionSimulationHorizonDays = 3;
    public IReadOnlyList<int> DecisionSimulationHorizonOptions { get; } = [3, 5, 10];
    [ObservableProperty] private string decisionSimulationStatus = "Choose an objective or departure time, then compare the consequences.";
    [ObservableProperty] private string decisionSimulationCalendarSyncStatus = "Calendar sync has not been checked.";
    [ObservableProperty] private bool decisionSimulationCalendarReady;
    [ObservableProperty] private bool decisionSimulationBusy;

    [RelayCommand]
    private async Task ShowDecisionSimulatorAsync()
    {
        DecisionSimulationCandidates = new ObservableCollection<MissionCandidate>(Workspace.RankedMissionCandidates
            .Where(x => x.IsActionable && x.TaskId is > 0 && x.EstimatedMinutes > 0)
            .GroupBy(x => x.TaskId)
            .Select(x => x.OrderByDescending(y => y.Score).First())
            .OrderBy(x => x.Rank <= 0 ? int.MaxValue : x.Rank)
            .Take(20));
        SelectedDecisionCandidate = DecisionSimulationCandidates.FirstOrDefault(x => x.TaskId == Workspace.CurrentMission.TaskId)
                                    ?? DecisionSimulationCandidates.FirstOrDefault();
        await RefreshDecisionSimulationAsync();
        new DecisionSimulatorWindow(this)
        {
            Owner = Application.Current.MainWindow
        }.ShowDialog();
    }

    [RelayCommand]
    private async Task RefreshDecisionSimulationAsync()
    {
        if (DecisionSimulationBusy) return;
        DecisionSimulationBusy = true;
        DecisionSimulationCalendarReady = false;
        DecisionSimulationStatus = "Reading the calendar and modelling three working days…";
        DecisionSimulationCalendarSyncStatus = "Checking Microsoft 365 calendar…";
        try
        {
            var now = DateTimeOffset.Now;
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var horizonDays = Math.Clamp(DecisionSimulationHorizonDays, 1, 10);
            var horizonEnd = GuardianDayScenarioEngine.WorkingHorizonEndExclusive(now.Date, horizonDays);
            IReadOnlyList<Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent> events;
            try
            {
                var offset = TimeZoneInfo.Local.GetUtcOffset(now.Date);
                _decisionSimulationCalendarRangeStart = new DateTimeOffset(now.Date, offset);
                _decisionSimulationCalendarRangeEnd = new DateTimeOffset(horizonEnd, TimeZoneInfo.Local.GetUtcOffset(horizonEnd));
                events = await _services.GetRequiredService<ICalendarService>().GetEventsAsync(
                    _decisionSimulationCalendarRangeStart, _decisionSimulationCalendarRangeEnd);
            }
            catch (Exception ex)
            {
                // The dashboard cache normally covers only its selected day. It must never be
                // presented as a complete multi-day scenario snapshot because doing so can place
                // proposed work over commitments later in the horizon.
                System.Diagnostics.Debug.WriteLine($"Decision-simulation calendar load failed: {ex}");
                DecisionScenarios = [];
                SelectedDecisionScenario = null;
                DecisionSimulationCalendarSyncStatus = "LIVE CALENDAR REQUIRED · no plan can be synced from a partial snapshot";
                DecisionSimulationStatus = "Guardian could not load the complete Microsoft 365 calendar horizon. Check the connection and re-run the comparison; no provisional schedule has been produced.";
                return;
            }

            var alreadyScheduled = events.Where(x => x.IsNekomataManaged).ToList();
            var projects = Workspace.Projects.ToDictionary(x => x.Id, x => x.Name);
            var proposalMemories = await _services.GetRequiredService<Nekomata.Data.Repositories.IGuardianMemoryRepository>().GetRecentAsync(100);
            var proposalPolicy = _services.GetRequiredService<Nekomata.Core.Guardian.Learning.GuardianProposalLearningPolicy>();
            var candidates = DecisionSimulationCandidates
                .Where(x => x.TaskId is > 0)
                .Where(x => !proposalPolicy.ShouldSuppress(x.Title, proposalMemories))
                .Select(x =>
                {
                    var taskId = x.TaskId!.Value;
                    var estimated = Math.Max(settings.MinimumFocusBlockMinutes,
                        x.PredictedActiveMinutes > 0 ? x.PredictedActiveMinutes + x.InterruptionOverheadMinutes : x.EstimatedMinutes);
                    var scheduled = alreadyScheduled
                        .Where(item => item.BodyPreview.Contains($"NEKOMATA:TASK:{taskId}", StringComparison.OrdinalIgnoreCase))
                        .Sum(item => Math.Max(0, (int)(item.End - item.Start).TotalMinutes));
                    return new GuardianScenarioWorkItem
                    {
                        TaskId = taskId, ProjectId = x.ProjectId, Title = x.Title,
                        ProjectName = x.ProjectId is long projectId && projects.TryGetValue(projectId, out var name) ? name : "",
                        SourceType = x.SourceType, Minutes = Math.Max(0, estimated - scheduled),
                        Score = x.Score, Rank = x.Rank, DueAt = x.DueAt,
                        IsCritical = x.RequiresImmediateAttention || x.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .Where(x => x.Minutes > 0)
                .ToList();
            var commitments = events.Select(x => new GuardianScenarioCommitment
            {
                Id = x.Id, Title = x.Subject, Start = x.Start, End = x.End,
                IsAllDay = x.IsAllDay, IsManaged = x.IsNekomataManaged
            }).ToList();
            var relationshipSignals = _relationshipDelayInsights.Take(3).Select(x =>
                $"Relationship signal: {x.ContactName} has an unusual response delay on '{x.Subject}' ({x.Confidence}% confidence)." ).ToList();
            var leaveAt = TimeSpan.TryParse(DecisionSimulationLeaveTime, out var parsed) ? parsed : settings.EndTime;
            DecisionSimulationLeaveTime = leaveAt.ToString(@"hh\:mm");
            var interruption = EstimateRemainingInterruptions(now, Math.Max(0, (settings.GetEnd(now.LocalDateTime) - now.LocalDateTime).TotalHours));
            var scenarios = _services.GetRequiredService<GuardianDayScenarioEngine>().Build(new()
            {
                Now = now, WorkdayStart = settings.StartTime, WorkdayEnd = settings.EndTime,
                LunchStart = settings.LunchStartTime, LunchMinutes = settings.LunchDurationMinutes,
                IncludeLunch = settings.IncludeLunchBreak, MinimumBlockMinutes = settings.MinimumFocusBlockMinutes,
                HorizonWorkingDays = horizonDays,
                InterruptionAllowanceMinutes = interruption.Minutes, CalendarLoaded = true,
                PreferredTaskId = SelectedDecisionCandidate?.TaskId, LeaveTodayAt = leaveAt,
                WorkItems = candidates, Commitments = commitments, RelationshipSignals = relationshipSignals
            });
            var invalid = scenarios
                .Select(scenario => new
                {
                    Scenario = scenario,
                    Conflict = GuardianDayScenarioEngine.FindFirstConflict(scenario, commitments)
                })
                .FirstOrDefault(item => item.Conflict is not null);
            if (invalid is not null)
                throw new InvalidOperationException(
                    $"The generated '{invalid.Scenario.Title}' scenario overlaps the loaded commitment '{invalid.Conflict!.Title}'.");
            DecisionScenarios = new ObservableCollection<GuardianDayScenario>(scenarios);
            SelectedDecisionScenario = DecisionScenarios.FirstOrDefault();
            DecisionSimulationCalendarReady = true;
            DecisionSimulationCalendarSyncStatus = $"LIVE CALENDAR READY · {commitments.Count} existing commitment(s) loaded across the {horizonDays}-workday horizon";
            DecisionSimulationStatus = $"Compared {scenarios.Count} plans across {horizonDays} working days using {candidates.Count} unscheduled objectives and {commitments.Count} fixed or existing calendar entries.";
        }
        catch (Exception ex)
        {
            DecisionSimulationCalendarSyncStatus = "CALENDAR SYNC UNAVAILABLE";
            DecisionSimulationStatus = $"The comparison could not be completed: {ex.Message}";
        }
        finally { DecisionSimulationBusy = false; }
    }

    public async Task<bool> PrepareDecisionScenarioForReviewAsync()
    {
        if (!DecisionSimulationCalendarReady)
        {
            DecisionSimulationStatus = "A complete live calendar horizon is required before this scenario can be reviewed for sync.";
            return false;
        }
        if (SelectedDecisionScenario is not { CanPrepare: true } scenario) return false;

        var selectedKey = scenario.Key;
        try
        {
            var latestEvents = await _services.GetRequiredService<ICalendarService>().GetEventsAsync(
                _decisionSimulationCalendarRangeStart, _decisionSimulationCalendarRangeEnd);
            var latestCommitments = latestEvents.Select(ToScenarioCommitment).ToList();
            var conflict = GuardianDayScenarioEngine.FindFirstConflict(scenario, latestCommitments);
            if (conflict is not null)
            {
                DecisionSimulationCalendarReady = false;
                DecisionSimulationStatus = $"The calendar changed: '{conflict.Title}' now occupies proposed time. Rebuilding the comparison from the latest Microsoft 365 calendar…";
                await RefreshDecisionSimulationAsync();
                if (!DecisionSimulationCalendarReady)
                    return false;
                SelectedDecisionScenario = DecisionScenarios.FirstOrDefault(item => item.Key == selectedKey)
                                           ?? DecisionScenarios.FirstOrDefault();
                DecisionSimulationStatus = $"Calendar changes were incorporated. Review the rebuilt '{SelectedDecisionScenario?.Title ?? "scenario"}' before syncing.";
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Decision-simulation pre-review validation failed: {ex}");
            DecisionSimulationCalendarReady = false;
            DecisionSimulationCalendarSyncStatus = "LIVE CALENDAR REVALIDATION FAILED";
            DecisionSimulationStatus = "Guardian could not revalidate the complete calendar immediately before review. Re-run the comparison; nothing has been changed.";
            return false;
        }

        PendingGuardianAction = GuardianScenarioCalendarPlanBuilder.Build(scenario);
        PendingGuardianProject = null;
        GuardianProposalVisible = true;
        GuardianPanelExpanded = true;
        GuardianResponse = PendingGuardianAction.Message;
        DecisionSimulationStatus = $"Prepared {scenario.Blocks.Count} calendar block(s) for individual review. Nothing has been synced yet.";
        return true;
    }

    public async Task<bool> SyncDecisionScenarioToCalendarAsync()
    {
        if (DecisionSimulationBusy || GuardianBusy || !DecisionSimulationCalendarReady ||
            SelectedDecisionScenario is not { CanPrepare: true } scenario)
            return false;

        if (!await PrepareDecisionScenarioForReviewAsync())
            return false;

        DecisionSimulationBusy = true;
        try
        {
            DecisionSimulationStatus = $"Revalidating and syncing {scenario.Blocks.Count} approved calendar block(s)…";

            // This uses the normal Guardian application pipeline. Decision-simulation plans are
            // validated as an atomic batch and rolled back if any block cannot be created.
            await CreateSelectedTasksAsync();
            var applied = PendingGuardianAction is null;
            DecisionSimulationStatus = applied
                ? $"Calendar synced · {scenario.Blocks.Count} focus block(s) applied. The batch can be undone from Calendar."
                : GuardianResponse;
            DecisionSimulationCalendarSyncStatus = applied
                ? $"SYNCED TO MICROSOFT 365 · {DateTime.Now:HH:mm:ss}"
                : "CALENDAR SYNC NEEDS ATTENTION";
            return applied;
        }
        finally
        {
            DecisionSimulationBusy = false;
        }
    }

    private static GuardianScenarioCommitment ToScenarioCommitment(
        Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent item) => new()
    {
        Id = item.Id,
        Title = item.Subject,
        Start = item.Start,
        End = item.End,
        IsAllDay = item.IsAllDay,
        IsManaged = item.IsNekomataManaged
    };
}
