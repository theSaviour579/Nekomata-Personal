using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool recoveryPlanVisible;
    [ObservableProperty] private GuardianRecoveryPlan recoveryPlan = new();
    [ObservableProperty] private bool recoveryPlanBusy;
    public bool RecoveryPlanReady => RecoveryPlan.CanApply && WorkloadForecast.NeedsRecovery;

    private void PrepareForecastRecoveryPlan()
    {
        if (!WorkloadForecast.NeedsRecovery || WorkloadForecast.ConfidencePercent < 65)
        {
            if (RecoveryPlan.ForecastDriven && !RecoveryPlanVisible) RecoveryPlan = new();
            OnPropertyChanged(nameof(RecoveryPlanReady));
            return;
        }
        var proposal = _services.GetRequiredService<Nekomata.Core.Guardian.Outcomes.GuardianRecoveryPlanner>()
            .BuildFromForecast(Workspace, WorkloadForecast, DateTime.Now);
        if (proposal is not null && !RecoveryPlanVisible) RecoveryPlan = proposal;
        OnPropertyChanged(nameof(RecoveryPlanReady));
    }

    private void EvaluateRecoveryPlan(GuardianInterruption interruption)
    {
        var proposal = _services.GetRequiredService<Nekomata.Core.Guardian.Outcomes.GuardianRecoveryPlanner>()
            .Build(Workspace, interruption, DateTime.Now);
        if (proposal is null) return;
        RecoveryPlan = proposal;
        RecoveryPlanVisible = true;
    }

    [RelayCommand]
    private async Task ApplyRecoveryPlanAsync()
    {
        if (RecoveryPlanBusy || !RecoveryPlan.CanApply) return;
        RecoveryPlanBusy = true;
        try
        {
            var reason = RecoveryPlan.ForecastDriven
                ? $"Recover forecast workload pressure while preserving {RecoveryPlan.ProtectedObjective} and fixed commitments. Forecast confidence: {RecoveryPlan.ForecastConfidence}%."
                : $"Recover from {RecoveryPlan.MinutesLost} interruption minutes while preserving {RecoveryPlan.ProtectedObjective} and fixed commitments.";
            var response = new GuardianActionResponse
            {
                ActionType = "recover_day", Message = reason, Confidence = 90,
                Changes =
                [
                    new GuardianChange
                    {
                        EntityType = "Task", EntityId = RecoveryPlan.DeferredTaskId, Property = "DueAt",
                        OldValue = "Today", NewValue = RecoveryPlan.DeferredUntil.ToString("O"),
                        Reason = $"Lower-ranked work displaced to recover {RecoveryPlan.MinutesRecovered} minutes.", Confidence = 90
                    }
                ]
            };
            var result = await _guardianApplyService.ApplyAsync(response);
            if (!result.Success) { ShowPaletteResponse(result.UserSummary); return; }

            var chain = await RecordDecisionChainAsync(RecoveryPlan.RecoverySummary, "Accepted");
            await RecordWorkdayEventAsync(new WorkdayEvent
            {
                CorrelationId = chain.CorrelationId, ParentEventId = chain.DecisionEventId, CausedByEventId = chain.DecisionEventId,
                EventType = "RecoveryPlanApplied", Category = "Planning", Title = "Guardian adapted",
                Narrative = $"Kept {RecoveryPlan.ProtectedObjective}; moved {RecoveryPlan.DeferredTaskTitle} to {RecoveryPlan.DeferredUntil:ddd dd MMM}; preserved fixed commitments. Projected finish after the change: {RecoveryPlan.ExpectedFinishAt:HH:mm}.",
                TaskId = RecoveryPlan.DeferredTaskId, Recommendation = RecoveryPlan.RecoverySummary,
                UserDecision = "Accepted", Outcome = "Applied", Significance = 85
            });
            RecoveryPlanVisible = false;
            OnPropertyChanged(nameof(RecoveryPlanReady));
            await _workspaceCoordinator.RefreshAsync();
            OnPropertyChanged(nameof(Workspace));
            GuardianResponse = $"Recovery applied. {RecoveryPlan.DeferredTaskTitle} moved to tomorrow. Expected finish: {RecoveryPlan.ExpectedFinishAt:HH:mm}. This action is recorded and can be undone from Activity.";
            GuardianPanelExpanded = true;
        }
        finally { RecoveryPlanBusy = false; }
    }

    [RelayCommand]
    private async Task DismissRecoveryPlanAsync()
    {
        var chain = await RecordDecisionChainAsync(RecoveryPlan.RecoverySummary, "Rejected");
        await RecordWorkdayEventAsync(new WorkdayEvent
        {
            CorrelationId = chain.CorrelationId, ParentEventId = chain.DecisionEventId,
            EventType = "RecoveryPlanRejected", Category = "Planning", Title = "Recovery plan declined",
            Narrative = RecoveryPlan.RecoverySummary, Recommendation = RecoveryPlan.RecoverySummary,
            UserDecision = "Rejected", Outcome = "No change", Significance = 60
        });
        RecoveryPlanVisible = false;
        OnPropertyChanged(nameof(RecoveryPlanReady));
    }
}
