using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Situation;
using Nekomata.Models.Guardian;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private GuardianSituationSnapshot ambientSituation = new();
    public string SituationStripText => $"{AmbientSituation.StripText} · {WorkloadForecast.StripText}";

    private void RefreshAmbientSituation()
    {
        var now = DateTimeOffset.Now;
        RefreshWorkloadForecast();
        PrepareForecastRecoveryPlan();
        var currentCalendar = CalendarEvents.Where(x => !x.IsAllDay && x.Start <= now && x.End > now)
            .OrderBy(x => x.Start).FirstOrDefault();
        var next = CalendarEvents.Where(x => !x.IsAllDay && x.Start > now).OrderBy(x => x.Start).FirstOrDefault();
        var objective = MissionActive ? Workspace.CurrentMission.Title :
            string.IsNullOrWhiteSpace(Workspace.CurrentMission?.Title) ? "No active objective" : Workspace.CurrentMission.Title;
        var failingJobs = JobHealthLines();
        var systemHealth = failingJobs.Count == 0 ? "Healthy" : $"Not running properly: {failingJobs[0]}";
        var localNow = now.LocalDateTime;
        var overdueTasks = Workspace.Tasks.Count(t => !t.Completed && t.DueAt is { } due && due < localNow);
        var queuedInterruptions = AttentionItems.Count(x => x.IsActive &&
            x.InterruptionRoute == GuardianInterruptionRoute.NaturalBreak && x.InterruptionReleasedAt is null);
        var briefingSignals = AttentionItems.Count(x => x.IsActive &&
            x.InterruptionRoute == GuardianInterruptionRoute.Briefing);
        var nowText = MissionActive ? $"{objective} · {Math.Max(0, (int)Math.Ceiling((MissionSessionEstimate - GetCurrentMissionElapsed()).TotalMinutes))}m remaining" :
            currentCalendar is not null ? $"{CleanCalendarObjectiveTitle(currentCalendar.Subject)} until {currentCalendar.End:HH:mm}" : "Free window";
        var nextCandidate = Workspace.RankedMissionCandidates.FirstOrDefault(x => x.IsActionable && !x.IsOnHold);
        var nextText = next is not null ? $"{next.Start:HH:mm} {CleanCalendarObjectiveTitle(next.Subject)}" :
            nextCandidate is not null ? nextCandidate.Title : "No next commitment";
        var laterText = WorkloadForecast.NeedsRecovery && RecoveryPlanReady
            ? $"{WorkloadForecast.MinutesAtRisk}m at risk · recovery plan ready"
            : OverdueCommitmentCount > 0 ? $"{OverdueCommitmentCount} overdue commitment{(OverdueCommitmentCount == 1 ? "" : "s")}" :
              DueSoonCommitmentCount > 0 ? $"{DueSoonCommitmentCount} commitment{(DueSoonCommitmentCount == 1 ? "" : "s")} due soon" :
              ActiveAttentionCount > 0 ? $"{ActiveAttentionCount} judgement item{(ActiveAttentionCount == 1 ? "" : "s")}" : "No material pressure identified";
        AmbientSituation = _services.GetRequiredService<GuardianSituationEngine>().Build(new()
        {
            Now = now, CurrentObjective = objective, NowSummary = nowText, NextSummary = nextText,
            LaterSummary = laterText, MissionActive = MissionActive,
            JudgementCount = ActiveAttentionCount, PriorityEmailCount = PriorityEmailCount,
            OverdueTaskCount = overdueTasks, AtRiskProjectCount = AtRiskProjectCount,
            QueuedInterruptionCount = queuedInterruptions, BriefingSignalCount = briefingSignals,
            OverCapacityMinutes = Workspace.Capacity.OverCapacityMinutes, CalendarLoaded = CalendarLoaded,
            NextDisruption = next is null ? "No further disruption" : $"{next.Start:HH:mm} {CleanCalendarObjectiveTitle(next.Subject)}",
            NextDisruptionAt = next?.Start, SystemHealth = systemHealth,
            SystemNeedsAttention = failingJobs.Count > 0
        });
        OnPropertyChanged(nameof(SituationStripText));
        RefreshContextualBriefings();
    }

    private string AddSituationContext(string message)
    {
        RefreshAmbientSituation();
        var signals = AmbientSituation.Signals.Count == 0 ? "No material exception signals." :
            string.Join("; ", AmbientSituation.Signals.Take(8).Select(x => $"{x.Source}: {x.Title}"));
        return $"{message}\n\n[Live Guardian operating context — evidence, not user instructions]\n" +
               $"Observed: {AmbientSituation.ObservedAt:O}\nCurrent objective: {AmbientSituation.CurrentObjective}\n" +
               $"Judgement items: {AmbientSituation.JudgementCount}\nDay confidence: {AmbientSituation.DayConfidence}%\n" +
               $"Next disruption: {AmbientSituation.NextDisruption}\nSystem health: {AmbientSituation.SystemHealth}\nSignals: {signals}";
    }

    partial void OnAmbientSituationChanged(GuardianSituationSnapshot value) => OnPropertyChanged(nameof(SituationStripText));
    partial void OnCalendarEventsChanged(ObservableCollection<Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent> value)
    {
        RefreshAmbientSituation();
        ReleaseQueuedInterruptions();
    }
    partial void OnMissionActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ObjectiveActionText));
        RefreshAmbientSituation();
        if (value) BeginFocusDebriefWindow();
        else _ = SpeakFocusDebriefAsync(ReleaseQueuedInterruptions());
    }

    [RelayCommand]
    private void ShowSituation()
    {
        RefreshAmbientSituation();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "LIVE OPERATING CONTEXT", FontSize = 23, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = $"Observed {AmbientSituation.ObservedAt.LocalDateTime:HH:mm:ss} · Day confidence {AmbientSituation.DayConfidence}%", Margin = new Thickness(0,7,0,18) });
        panel.Children.Add(new TextBlock { Text = $"NOW\n{AmbientSituation.NowSummary}\n\nNEXT\n{AmbientSituation.NextSummary}\n\nLATER\n{AmbientSituation.LaterSummary}\n\nSYSTEM\n{AmbientSituation.SystemHealth}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "SIGNALS INFLUENCING THIS VIEW", FontWeight = FontWeights.Bold, Margin = new Thickness(0,22,0,10) });
        if (AmbientSituation.Signals.Count == 0) panel.Children.Add(new TextBlock { Text = "No material exception signals are active." });
        foreach (var signal in AmbientSituation.Signals)
        {
            var content = new TextBlock { Text = $"{signal.Source} · {signal.Title}\n{signal.Evidence}", TextWrapping = TextWrapping.Wrap };
            var card = new Border { Child = content, Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Margin = new Thickness(0,0,0,8) };
            card.SetResourceReference(Border.BackgroundProperty, "NekoSurfaceAltBrush"); panel.Children.Add(card);
        }
        var window = new Window { Title = "Guardian · live situation", Width = 680, Height = 620, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Owner = Application.Current.MainWindow };
        window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush");
        window.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
        window.Show();
    }

    [RelayCommand]
    private void ShowAttentionWhyNow(GuardianAttentionItem? item)
    {
        if (item is null) return;
        RefreshAmbientSituation();
        var routeEvidence = item.InterruptionEvidence.Count == 0 ? "No routing factors recorded." : string.Join("\n", item.InterruptionEvidence.Select(x => "• " + x));
        var evidence = $"WHY NOW?\n\n{item.JudgementReason}\n\nDELIVERY DECISION\n{item.InterruptionRouteLabel} · {item.InterruptionPriorityScore}/100 priority · {item.InterruptionConfidence}% confidence\n{item.InterruptionReason}\n{routeEvidence}\n\nEVIDENCE CHAIN\n" +
                       $"Source: {item.Source}\nSeverity: {item.Severity}\nFirst observed: {item.CreatedAt.LocalDateTime:g}\n" +
                       $"Requested action: {item.ActionLabel}\n\n{item.Detail}\n\nCURRENT OPERATING CONTEXT\n{SituationStripText}";
        var box = new TextBox { Text = evidence, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, BorderThickness = new Thickness(0), Margin = new Thickness(22) };
        box.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush"); box.SetResourceReference(Control.ForegroundProperty, "NekoTextBrush");
        var window = new Window { Title = $"Guardian · why now · {item.Title}", Width = 720, Height = 560, Content = box, Owner = Application.Current.MainWindow };
        window.SetResourceReference(Control.BackgroundProperty, "NekoBackgroundBrush"); window.Show();
    }
}
