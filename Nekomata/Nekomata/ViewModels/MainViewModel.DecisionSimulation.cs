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
using System.Security.Cryptography;
using System.Text;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private ObservableCollection<GuardianDayScenario> decisionScenarios = [];
    [ObservableProperty] private GuardianDayScenario? selectedDecisionScenario;
    [ObservableProperty] private ObservableCollection<MissionCandidate> decisionSimulationCandidates = [];
    [ObservableProperty] private MissionCandidate? selectedDecisionCandidate;
    [ObservableProperty] private string decisionSimulationLeaveTime = "16:00";
    [ObservableProperty] private int decisionSimulationHorizonDays = 3;
    public IReadOnlyList<int> DecisionSimulationHorizonOptions { get; } = [3, 5, 10];
    [ObservableProperty] private string decisionSimulationStatus = "Choose an objective or departure time, then compare the consequences.";
    [ObservableProperty] private bool decisionSimulationBusy;

    [RelayCommand]
    private async Task ShowDecisionSimulatorAsync()
    {
        DecisionSimulationCandidates = new(Workspace.RankedMissionCandidates
            .Where(x => x.IsActionable && x.EstimatedMinutes > 0 && (x.TaskId is > 0 || !string.IsNullOrWhiteSpace(x.SourceRecordId)))
            .GroupBy(ScenarioCandidateKey, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderByDescending(y => y.Score).First())
            .OrderBy(x => x.Rank <= 0 ? int.MaxValue : x.Rank).Take(20));
        SelectedDecisionCandidate = DecisionSimulationCandidates.FirstOrDefault(x => x.TaskId == Workspace.CurrentMission.TaskId) ?? DecisionSimulationCandidates.FirstOrDefault();
        await RefreshDecisionSimulationAsync();
        new DecisionSimulatorWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private async Task RefreshDecisionSimulationAsync()
    {
        if (DecisionSimulationBusy) return;
        DecisionSimulationBusy = true;
        DecisionSimulationStatus = "Reading the calendar and modelling the options…";
        try
        {
            var now = DateTimeOffset.Now;
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var horizon = Math.Clamp(DecisionSimulationHorizonDays, 1, 10);
            var horizonEnd = AddWorkingDays(now.Date, horizon - 1).AddDays(1);
            IReadOnlyList<Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent> events;
            var loaded = false;
            try
            {
                events = await _services.GetRequiredService<ICalendarService>().GetEventsAsync(new(now.Date, TimeZoneInfo.Local.GetUtcOffset(now.Date)), new(horizonEnd, TimeZoneInfo.Local.GetUtcOffset(horizonEnd)));
                loaded = true;
            }
            catch { events = CalendarEvents.ToList(); loaded = CalendarLoaded; }
            var projects = Workspace.Projects.ToDictionary(x => x.Id, x => x.Name);
            var work = DecisionSimulationCandidates.Select(x => new GuardianScenarioWorkItem
            {
                TaskId = ScenarioTaskId(x), ProjectId = x.ProjectId, Title = x.Title,
                ProjectName = x.ProjectId is long id && projects.TryGetValue(id, out var name) ? name : "",
                Minutes = Math.Max(settings.MinimumFocusBlockMinutes, x.EstimatedMinutes), Score = x.Score, Rank = x.Rank, DueAt = x.DueAt,
                IsCritical = x.RequiresImmediateAttention || x.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase)
            }).ToList();
            var commitments = events.Select(x => new GuardianScenarioCommitment { Id = x.Id, Title = x.Subject, Start = x.Start, End = x.End, IsAllDay = x.IsAllDay }).ToList();
            var leaveAt = TimeSpan.TryParse(DecisionSimulationLeaveTime, out var parsed) ? parsed : settings.EndTime;
            DecisionSimulationLeaveTime = leaveAt.ToString(@"hh\:mm");
            var scenarios = _services.GetRequiredService<GuardianDayScenarioEngine>().Build(new()
            {
                Now = now, WorkdayStart = settings.StartTime, WorkdayEnd = settings.EndTime, LunchStart = settings.LunchStartTime,
                LunchMinutes = settings.LunchDurationMinutes, IncludeLunch = settings.IncludeLunchBreak, MinimumBlockMinutes = settings.MinimumFocusBlockMinutes,
                HorizonWorkingDays = horizon, CalendarLoaded = loaded,
                PreferredTaskId = SelectedDecisionCandidate is null ? null : ScenarioTaskId(SelectedDecisionCandidate), LeaveTodayAt = leaveAt,
                WorkItems = work, Commitments = commitments
            });
            DecisionScenarios = new(scenarios);
            SelectedDecisionScenario = DecisionScenarios.FirstOrDefault();
            DecisionSimulationStatus = $"Compared {scenarios.Count} plans across {horizon} working days using {work.Count} objectives and {commitments.Count} calendar entries.";
        }
        catch (Exception ex) { DecisionSimulationStatus = $"The comparison could not be completed: {ex.Message}"; }
        finally { DecisionSimulationBusy = false; }
    }

    public Task<bool> PrepareDecisionScenarioForReviewAsync()
    {
        if (SelectedDecisionScenario is not { CanPrepare: true } scenario) return Task.FromResult(false);
        PendingGuardianAction = new GuardianActionResponse
        {
            ActionType = "decision_simulation_plan", Confidence = scenario.ConfidencePercent,
            Message = $"{scenario.Title} is ready for review. {scenario.FinishLabel}; {scenario.RiskLabel}. Nothing has been changed yet.",
            Changes = scenario.Blocks.Select(block => new GuardianChange { Selected = true, EntityType = "Calendar", EntityId = block.TaskId, Property = "CreateFocusBlock", OldValue = "", NewValue = $"{block.Start:O}|{block.End:O}|{block.Title}", Reason = $"Decision simulation '{scenario.Title}'.", Confidence = scenario.ConfidencePercent }).ToList()
        };
        PendingGuardianProject = null; GuardianProposalVisible = true; GuardianPanelExpanded = true; GuardianResponse = PendingGuardianAction.Message;
        return Task.FromResult(true);
    }

    private static DateTime AddWorkingDays(DateTime start, int count)
    {
        var date = start.Date;
        while (count-- > 0) { date = date.AddDays(1); while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1); }
        return date;
    }

    private static string ScenarioCandidateKey(MissionCandidate candidate) =>
        candidate.TaskId is > 0 ? $"task:{candidate.TaskId}" : $"{candidate.SourceType}:{candidate.SourceRecordId}";

    private static long ScenarioTaskId(MissionCandidate candidate)
    {
        if (candidate.TaskId is > 0) return candidate.TaskId.Value;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ScenarioCandidateKey(candidate)));
        var value = BitConverter.ToInt64(bytes, 0) & long.MaxValue;
        return value == 0 ? -1 : -value;
    }
}
