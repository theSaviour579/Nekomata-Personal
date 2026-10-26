using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Missions;
using Nekomata.Core.Planning;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using System.Collections.ObjectModel;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool interruptionPanelVisible;
    [ObservableProperty] private string interruptionInput = "";
    [ObservableProperty] private string interruptionResumeNote = "";
    [ObservableProperty] private string interruptionHeadline = "INTERRUPTION TRIAGE";
    [ObservableProperty] private string interruptionAdvice = "Describe the interruption. Guardian will compare it with your active mission.";
    [ObservableProperty] private string interruptionRecommendation = "";
    public ObservableCollection<GuardianInterruption> InterruptionsToday { get; } = [];
    private DateTimeOffset _interruptionOpenedAt;
    private bool _pausedForInterruption;
    private Guid? _pendingRoleCorrectionId;
    private string _pendingRoleCorrectionDecision = "";

    public int InterruptionCountToday => InterruptionsToday.Count;
    public int InterruptionMinutesLostToday => InterruptionsToday.Sum(x => x.LostMinutes);
    public int InterruptionsQueuedToday => InterruptionsToday.Count(x => x.Decision == "Queued");
    public int InterruptionsDelegatedToday => InterruptionsToday.Count(x => x.Decision == "Delegated");
    public int InterruptionsUrgentToday => InterruptionsToday.Count(x => x.Decision == "Urgent");
    public string InterruptionSummary =>
        $"{InterruptionCountToday} interruptions · {InterruptionMinutesLostToday} mins lost · {InterruptionsQueuedToday} queued · {InterruptionsDelegatedToday} delegated · {InterruptionsUrgentToday} urgent";
    public GuardianInterruption? LatestResumePoint => InterruptionsToday.FirstOrDefault(x => x.Decision == "Urgent" && x.ResumedAt is null && !string.IsNullOrWhiteSpace(x.MissionTitle));
    public bool HasResumePoint => LatestResumePoint is not null;

    private static bool ShouldDisplayInterruption(GuardianAttentionItem item) =>
        item.InterruptionRoute == GuardianInterruptionRoute.Immediate || item.InterruptionReleasedAt is not null;

    // Returns what was released (empty when nothing was, or when it isn't a natural break yet),
    // so the focus debrief can tell the user what came in.
    private List<GuardianAttentionItem> ReleaseQueuedInterruptions()
    {
        var now = DateTimeOffset.Now;
        if (MissionActive || CalendarEvents.Any(x => x.Start <= now && x.End > now)) return [];
        var released = AttentionItems.Where(x => x.IsActive && x.InterruptionRoute == GuardianInterruptionRoute.NaturalBreak &&
                                                  x.InterruptionReleasedAt is null).ToList();
        if (released.Count == 0) return released;
        foreach (var item in released)
        {
            item.InterruptionReleasedAt = now;
            _ = RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "InterruptionReleased", Category = item.Source, Title = item.Title,
                Narrative = $"Released at a natural break. {item.InterruptionReason}",
                ExternalId = item.ContextId, Outcome = "Surfaced", Significance = 45
            });
        }
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
        NotifyAttention();
        return released;
    }

    [RelayCommand]
    private void OpenInterruption()
    {
        if (!MissionActive || _activeMission is null) return;
        _pausedForInterruption = !MissionPaused;
        if (_pausedForInterruption) ToggleMissionPause();
        _interruptionOpenedAt = DateTimeOffset.Now;
        InterruptionInput = "";
        InterruptionResumeNote = _activeMission.Description;
        InterruptionHeadline = "INTERRUPTION TRIAGE";
        InterruptionAdvice = $"Your {_activeMission.Title} focus session is preserved while Guardian triages this input.";
        InterruptionRecommendation = "";
        InterruptionPanelVisible = true;
    }

    [RelayCommand]
    private async Task TriageInterruptionAsync()
    {
        if (_activeMission is null || string.IsNullOrWhiteSpace(InterruptionInput)) return;
        var remaining = Math.Max(0, (int)Math.Ceiling((MissionSessionEstimate - GetCurrentMissionElapsed()).TotalMinutes));
        var result = _services.GetRequiredService<InterruptionTriageService>()
            .Triage(InterruptionInput, _activeMission, remaining);
        var roleProtection = await _services.GetRequiredService<RoleProtectionService>()
            .EvaluateAsync(InterruptionInput, _activeMission, remaining);
        if (roleProtection.Applies && result.Decision != "Urgent")
        {
            result = new InterruptionTriage(roleProtection.Decision, roleProtection.Headline,
                roleProtection.Explanation, roleProtection.ConfidencePercent);
            var type = roleProtection.Decision == "Delegated"
                ? RoleCorrectionType.DelegateRecurringWork
                : RoleCorrectionType.ReserveStrategicBlock;
            var action = new RoleCorrectiveAction(Guid.NewGuid(), type,
                type == RoleCorrectionType.DelegateRecurringWork ? "technical" : "strategic",
                roleProtection.Headline, 30, roleProtection.ConfidencePercent, 2,
                roleProtection.ConfidencePercent, roleProtection.Explanation,
                ["Role-protection interruption triage", $"Active mission: {_activeMission.Title}", $"Recommended decision: {roleProtection.Decision}"]);
            _pendingRoleCorrectionId = await _services.GetRequiredService<CorrectionOutcomeEvaluator>()
                .RecordRecommendationAsync(action, RoleAlignment?.Score ?? 0, "Protect-my-role interruption triage", deduplicateDaily: false);
            _pendingRoleCorrectionDecision = roleProtection.Decision;
        }
        InterruptionHeadline = result.Headline;
        InterruptionAdvice = result.Explanation;
        InterruptionRecommendation = result.Decision;
        var notification = _services.GetRequiredService<Nekomata.UI.Views.GuardianInterruptionNotificationWindow>();
        notification.Owner = null;
        notification.Show();
        notification.Activate();
    }

    [RelayCommand]
    private async Task ApplyInterruptionDecisionAsync(string decision)
    {
        if (_activeMission is null || string.IsNullOrWhiteSpace(InterruptionInput)) return;
        var mission = _activeMission;
        var elapsed = GetCurrentMissionElapsed();
        var remaining = Math.Max(0, (int)Math.Ceiling((MissionSessionEstimate - elapsed).TotalMinutes));
        var lost = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.Now - _interruptionOpenedAt).TotalMinutes));
        var person = InterruptionInput.Split('-', 2)[0].Trim();
        var record = new GuardianInterruption
        {
            InterruptedAt = _interruptionOpenedAt, ResolvedAt = DateTimeOffset.Now,
            Description = InterruptionInput.Trim(), Person = person.Length <= 60 ? person : "",
            Decision = decision, Reason = InterruptionAdvice, MissionTitle = mission.Title,
            TaskId = mission.TaskId, ProjectId = mission.ProjectId, WorkType = mission.SourceType,
            ElapsedMinutes = (int)Math.Floor(elapsed.TotalMinutes), RemainingMinutes = remaining,
            LostMinutes = lost, ResumeNote = InterruptionResumeNote.Trim()
        };
        record.Id = await _services.GetRequiredService<IGuardianInterruptionRepository>().AddAsync(record);
        if (_pendingRoleCorrectionId is Guid correctionId)
        {
            var accepted = decision.Equals(_pendingRoleCorrectionDecision, StringComparison.OrdinalIgnoreCase);
            await _services.GetRequiredService<CorrectionOutcomeEvaluator>().RecordDecisionAsync(
                correctionId, accepted ? RoleCorrectionDecision.Approved : RoleCorrectionDecision.Rejected,
                accepted ? $"User accepted Guardian's {decision.ToLowerInvariant()} role-protection recommendation."
                         : $"User chose {decision} instead of Guardian's {_pendingRoleCorrectionDecision} recommendation.",
                occurred: accepted && decision == "Queued");
            _pendingRoleCorrectionId = null;
            _pendingRoleCorrectionDecision = "";
        }
        var chain = await RecordDecisionChainAsync(InterruptionAdvice, decision);
        await RecordWorkdayEventAsync(new WorkdayEvent
        {
            CorrelationId = chain.CorrelationId, ParentEventId = chain.DecisionEventId,
            CausedByEventId = chain.DecisionEventId,
            EventType = "PlanDisrupted", Category = "Interruption", Title = "Plan disrupted",
            Narrative = $"{record.Description} consumed {record.LostMinutes} minute{(record.LostMinutes == 1 ? "" : "s")}. Guardian {decision.ToLowerInvariant()} the request and preserved {record.MissionTitle} with {record.RemainingMinutes} minutes remaining.",
            TaskId = record.TaskId, ProjectId = record.ProjectId, UserDecision = decision,
            Recommendation = InterruptionAdvice, Outcome = decision, Significance = decision == "Urgent" ? 85 : 65
        });

        if (decision == "Queued" || decision == "Delegated")
        {
            RaiseAttention($"interrupt:{record.Id}", "INTERRUPTION", decision == "Delegated" ? "Info" : "Warning",
                decision == "Delegated" ? $"Delegate: {record.Description}" : record.Description,
                decision == "Delegated" ? "Create or assign this follow-up without abandoning the current focus block." : "Guardian queued this interruption behind the active mission.",
                "open_dashboard");
            InterruptionPanelVisible = false;
            if (_pausedForInterruption && MissionPaused) ToggleMissionPause();
        }
        else
        {
            InterruptionPanelVisible = false;
            await HoldMissionForLaterAsync();
        }

        await RefreshInterruptionsAsync();
        EvaluateRecoveryPlan(record);
    }

    [RelayCommand]
    private async Task CancelInterruptionAsync()
    {
        if (_pendingRoleCorrectionId is Guid correctionId)
        {
            await _services.GetRequiredService<CorrectionOutcomeEvaluator>().RecordDecisionAsync(
                correctionId, RoleCorrectionDecision.Rejected, "User closed role-protection triage without applying the recommendation.");
            _pendingRoleCorrectionId = null;
            _pendingRoleCorrectionDecision = "";
        }
        InterruptionPanelVisible = false;
        if (_pausedForInterruption && MissionPaused) ToggleMissionPause();
    }

    [RelayCommand]
    private async Task ResumeInterruptedMissionAsync()
    {
        var resume = LatestResumePoint;
        if (resume is null || MissionActive) return;
        var candidate = Workspace.RankedMissionCandidates.FirstOrDefault(x =>
            (resume.TaskId is not null && x.TaskId == resume.TaskId) ||
            (resume.ProjectId is not null && x.ProjectId == resume.ProjectId));
        if (candidate is null)
        {
            GuardianResponse = $"Resume point retained for '{resume.MissionTitle}', but its work item is not currently actionable. Next: {resume.ResumeNote}";
            GuardianPanelExpanded = true;
            return;
        }
        Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(candidate);
        OnPropertyChanged(nameof(Workspace));
        await BeginMissionAsync();
        GuardianResponse = $"Resumed: {resume.MissionTitle}. Next: {resume.ResumeNote}. Previous estimate remaining: {resume.RemainingMinutes} mins.";
        await _services.GetRequiredService<IGuardianInterruptionRepository>().MarkResumedAsync(resume.Id);
        await RefreshInterruptionsAsync();
    }

    private async Task RefreshInterruptionsAsync()
    {
        var records = await _services.GetRequiredService<IGuardianInterruptionRepository>().GetTodayAsync();
        InterruptionsToday.Clear();
        foreach (var record in records) InterruptionsToday.Add(record);
        OnPropertyChanged(nameof(InterruptionCountToday));
        OnPropertyChanged(nameof(InterruptionMinutesLostToday));
        OnPropertyChanged(nameof(InterruptionsQueuedToday));
        OnPropertyChanged(nameof(InterruptionsDelegatedToday));
        OnPropertyChanged(nameof(InterruptionsUrgentToday));
        OnPropertyChanged(nameof(InterruptionSummary));
        OnPropertyChanged(nameof(LatestResumePoint));
        OnPropertyChanged(nameof(HasResumePoint));
    }
}
