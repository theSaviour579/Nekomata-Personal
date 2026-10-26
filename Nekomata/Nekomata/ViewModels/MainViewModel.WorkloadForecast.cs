using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Forecasting;
using Nekomata.Models.Common;
using Nekomata.Models.Guardian;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private GuardianWorkloadForecast workloadForecast = new();

    private void RefreshWorkloadForecast()
    {
        var now = DateTimeOffset.Now;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var end = settings.GetEnd(now.LocalDateTime);
        var remainingHours = Math.Max(0, (end - now.LocalDateTime).TotalHours);
        var interruptionEvidence = EstimateRemainingInterruptions(now, remainingHours);
        var nonWorking = RemainingLunchMinutes(settings, now.LocalDateTime);
        var items = Workspace.RankedMissionCandidates
            .Where(x => x.IsActionable && x.DueAt?.Date <= now.Date && x.TaskId is > 0)
            .GroupBy(x => x.TaskId)
            .Select(x => x.OrderByDescending(candidate => candidate.Score).First())
            .Select(x => new GuardianWorkloadItem
            {
                TaskId = x.TaskId, Title = x.Title,
                Minutes = Math.Max(1, x.PredictedActiveMinutes > 0 ? x.PredictedActiveMinutes + x.InterruptionOverheadMinutes : x.EstimatedMinutes),
                Score = x.Score, IsCritical = x.RequiresImmediateAttention || x.Priority.Equals(TaskPriorities.Critical, StringComparison.OrdinalIgnoreCase),
                DueAt = x.DueAt
            }).ToList();
        WorkloadForecast = _services.GetRequiredService<GuardianWorkloadForecastEngine>().Build(new()
        {
            Now = now, WorkdayEnd = end, CalendarLoaded = CalendarLoaded,
            RemainingWorkableMinutes = Workspace.Capacity.AvailableMinutesToday + Workspace.Capacity.ScheduledMinutesRemaining,
            FixedCommitmentMinutes = Workspace.Capacity.ScheduledMinutesRemaining,
            PlannedWorkMinutes = Workspace.Capacity.PlannedMinutesToday,
            InterruptionAllowanceMinutes = interruptionEvidence.Minutes,
            InterruptionEvidenceCount = interruptionEvidence.EvidenceCount,
            NonWorkingMinutesRemaining = nonWorking, WorkItems = items
        });
        OnPropertyChanged(nameof(SituationStripText));
    }

    private (int Minutes, int EvidenceCount) EstimateRemainingInterruptions(DateTimeOffset now, double remainingHours)
    {
        if (remainingHours <= 0) return (0, 0);
        var day = now.DayOfWeek.ToString();
        var windows = Workspace.Outcomes.Insights.Where(x => x.Kind == "InterruptionWindow" &&
            x.WorkType.Equals(day, StringComparison.OrdinalIgnoreCase) && x.ObservedRatio is not null).ToList();
        var damage = Workspace.Outcomes.Insights.Where(x => x.Kind == "FragmentationCost" && x.ObservedRatio is not null).ToList();
        if (windows.Count > 0 && damage.Count > 0)
        {
            var hourlyRate = windows.Average(x => x.ObservedRatio!.Value);
            var averageLoss = damage.Average(x => x.ObservedRatio!.Value);
            return (Math.Clamp((int)Math.Ceiling(hourlyRate * remainingHours * averageLoss), 0, 180),
                windows.Sum(x => x.SampleSize) + damage.Sum(x => x.SampleSize));
        }

        if (InterruptionsToday.Count == 0) return (0, 0);
        var start = _services.GetRequiredService<WorkingDaySettings>().GetStart(now.LocalDateTime);
        var elapsedHours = Math.Max(1, (now.LocalDateTime - start).TotalHours);
        var observedRate = InterruptionsToday.Count / elapsedHours;
        var observedLoss = Math.Max(1, InterruptionsToday.Average(x => x.LostMinutes));
        return (Math.Clamp((int)Math.Ceiling(observedRate * remainingHours * observedLoss), 0, 180), InterruptionsToday.Count);
    }

    private static int RemainingLunchMinutes(WorkingDaySettings settings, DateTime now)
    {
        if (!settings.IncludeLunchBreak || now >= settings.GetLunchEnd(now)) return 0;
        var from = now > settings.GetLunchStart(now) ? now : settings.GetLunchStart(now);
        return Math.Max(0, (int)(settings.GetLunchEnd(now) - from).TotalMinutes);
    }

    private void CheckWorkloadForecastAttention()
    {
        const string prefix = "forecast:capacity:";
        if (!WorkloadForecast.NeedsRecovery || WorkloadForecast.ConfidencePercent < 65)
        {
            ClearTransientAttentionByPrefix(prefix);
            return;
        }
        var key = $"{prefix}{DateTime.Today:yyyy-MM-dd}";
        RaiseAttention(key, "WORKLOAD FORECAST", WorkloadForecast.MinutesAtRisk >= 60 ? "High" : "Warning",
            $"Forecast: {WorkloadForecast.MinutesAtRisk} minutes at risk",
            $"{WorkloadForecast.Summary} {WorkloadForecast.Evidence}", "preview_recovery_plan",
            expiresAt: new DateTimeOffset(_services.GetRequiredService<WorkingDaySettings>().GetEnd(DateTime.Today)));
    }

    [RelayCommand]
    private void PreviewForecastRecovery()
    {
        RefreshWorkloadForecast();
        var proposal = _services.GetRequiredService<Nekomata.Core.Guardian.Outcomes.GuardianRecoveryPlanner>()
            .BuildFromForecast(Workspace, WorkloadForecast, DateTime.Now);
        if (proposal is null)
        {
            System.Windows.MessageBox.Show("The forecast does not currently identify a safe lower-ranked task to move. No changes were made.",
                "Guardian workload forecast");
            return;
        }
        RecoveryPlan = proposal;
        RecoveryPlanVisible = true;
    }
}
