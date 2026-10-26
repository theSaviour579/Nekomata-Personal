using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Interruptions;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private readonly DispatcherTimer _attentionTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Dictionary<string, DateTimeOffset> _resolvedAttentionKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suppressedInterruptionKeys = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private ObservableCollection<GuardianAttentionItem> attentionItems = [];

    public IReadOnlyList<GuardianAttentionItem> VisibleAttentionItems =>
        AttentionItems
            .Where(item => item.IsActive)
            .Where(item => item.RequiresHumanJudgement)
            .Where(ShouldDisplayInterruption)
            .Where(item => item.ExpiresAt == null || item.ExpiresAt > DateTimeOffset.Now)
            .OrderBy(item => AttentionSeverityRank(item.Severity))
            .ThenByDescending(item => item.CreatedAt)
            .ToList();

    public int ActiveAttentionCount => VisibleAttentionItems.Count;
    public bool HasActiveAttention => ActiveAttentionCount > 0;
    public bool HasAttentionHistory => VisibleAttentionItems.Count > 0;
    /// <summary>The top item's area, for the one-line signal under the Needs-you count on Home.</summary>
    public string AttentionSummaryLine => VisibleAttentionItems.Count == 0
        ? "ALL CLEAR"
        : VisibleAttentionItems[0].Source.ToUpperInvariant();

    public string AttentionButtonLabel => HasActiveAttention ? $"ATTENTION ({ActiveAttentionCount})" : "ATTENTION";

    private void InitialiseAttentionCentre()
    {
        LoadAttentionItems();
        _attentionTimer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(RoutinePromptStatus));
            WakeSnoozedAttentionItems();
        };
        _attentionTimer.Start();
    }

    [RelayCommand]
    private void ShowAttention()
    {
        WorkspaceMode = WorkspaceMode.Attention;
        var top = VisibleAttentionItems.FirstOrDefault();
        if (top is not null) TrackSurfacedEntity("AttentionItem", top.Key, top.Title, "ShowAttention");
    }

    [RelayCommand]
    private async Task ActAttentionItemAsync(GuardianAttentionItem? item)
    {
        if (item is null) return;
        // Opened = Guardian's item was worth looking at (the scorecard's "useful" count).
        _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "AttentionOpened", Category = item.Source, Title = "Attention opened", Narrative = item.Title,
            ExternalId = string.IsNullOrWhiteSpace(item.ContextId) ? item.Key : item.ContextId, Significance = 30
        });
        switch (item.ActionKind)
        {
            case "personal_followup":
                OpenPersonalFollowups(item.ContextId);
                break;
            case "meeting_capture": ShowMeetingCapture(item); break;
            case "meeting_preparation": ShowMeetingPreparation(item); break;
            case "adaptive_replan":
                ReviewAdaptiveReplan(item);
                break;
            case "review_project_hold":
                await ReviewProjectHoldAsync(item);
                break;
            case "open_email":
                if (!string.IsNullOrWhiteSpace(item.WebLink))
                    Process.Start(new ProcessStartInfo(item.WebLink) { UseShellExecute = true });
                else
                    WorkspaceMode = WorkspaceMode.Email;
                break;
            case "self_check":
                await SelfCheckAsync(onDemand: true);
                break;
            case "duty":
                await OpenDutyAsync(item.ContextId);
                break;
            case "open_planner":
                OpenPlanner(item.ContextId);
                break;
            case "time_week":
                await WhereDidMyTimeGoAsync(week: true);
                break;
            case "meeting_review":
                await MeetingReviewAsync();
                break;
            case "none":
                break;
            case "setup_check":
                await SetupCheckAsync();
                break;
            case "job_health":
                await JobHealthReportAsync();
                break;
            case "week_review":
                ShowWeekReview();
                break;
            case "show_feedback":
                await ShowFeedbackAsync();
                break;
            case "review_approval":
                await ReviewApprovalAsync(item.ContextId);
                break;
            case "project_status":
                await ProjectStatusAsync(item.ContextId);
                break;
            case "review_invite":
                await ReviewInviteAsync(item.ContextId);
                break;
            case "open_teams":
                if (!string.IsNullOrWhiteSpace(item.WebLink)) OpenTeamsLink(item.WebLink);
                else await TeamsCatchUpAsync();
                break;
            case "start_calendar":
                WorkspaceMode = WorkspaceMode.Calendar;
                if (_planAlertEvent is not null && string.Equals(_planAlertEvent.Id, item.ContextId, StringComparison.OrdinalIgnoreCase))
                    await StartPlanAlertAsync();
                break;
            case "open_calendar":
                WorkspaceMode = WorkspaceMode.Calendar;
                break;
            case "open_diagnostics":
                await ShowDiagnosticsAsync();
                break;
            case "preview_recovery_plan":
                PreviewForecastRecovery();
                break;
            case "spotify_arrival_swap":
                await SwapToArrivalMixFromAttentionAsync(item);
                break;
            default:
                WorkspaceMode = WorkspaceMode.Dashboard;
                break;
        }
    }

    [RelayCommand]
    private void SnoozeAttentionItem(GuardianAttentionItem? item)
    {
        if (item is null) return;
        item.Status = "Snoozed";
        item.SnoozedUntil = DateTimeOffset.Now.AddMinutes(15);
        item.MeetingPromptShown = false;
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    [RelayCommand]
    private void ResolveAttentionItem(GuardianAttentionItem? item)
    {
        if (item is null) return;
        item.CompletionEvidence = "Handled or no longer requires judgement.";
        item.Status = "Resolved";
        item.SnoozedUntil = null;
        item.ResolvedAt = DateTimeOffset.Now;
        RememberResolvedAttention(item.Key);
        RecordAttentionResolution(item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    [RelayCommand]
    private void DismissAttentionItem(GuardianAttentionItem? item)
    {
        if (item is null) return;
        item.CompletionEvidence = "Dismissed as not useful.";
        item.Status = "Resolved";
        item.SnoozedUntil = null;
        item.ResolvedAt = DateTimeOffset.Now;
        RememberResolvedAttention(item.Key);
        RecordAttentionResolution(item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    [RelayCommand]
    private void ClearResolvedAttention()
    {
        foreach (var item in AttentionItems.Where(item =>
                     item.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)).ToList())
            AttentionItems.Remove(item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private void RaiseAttention(
        string key,
        string source,
        string severity,
        string title,
        string detail,
        string actionKind,
        string? contextId = null,
        string? webLink = null,
        DateTimeOffset? expiresAt = null,
        Guid? decisionOutcomeId = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (_resolvedAttentionKeys.TryGetValue(key, out var resolvedAt) && resolvedAt > DateTimeOffset.Now.AddDays(-30)) return;
        var now = DateTimeOffset.Now;
        var routing = _services.GetRequiredService<GuardianInterruptionPolicy>().Evaluate(new()
        {
            Source = source, Severity = severity, Title = title, Detail = detail, ActionKind = actionKind,
            FocusActive = MissionActive, CalendarCommitmentActive = CalendarEvents.Any(x => x.Start <= now && x.End > now),
            CurrentObjective = Workspace.CurrentMission?.Title ?? "", Deadline = expiresAt,
            PreferenceAdjustment = InterruptionPreferenceFor(source, actionKind)
        }, now);
        routing = ApplyOffHours(routing, key, severity, title);
        if (routing.Route == GuardianInterruptionRoute.Suppress)
        {
            var suppressed = AttentionItems.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (suppressed is not null)
            {
                AttentionItems.Remove(suppressed);
                SaveAttentionItems();
                RaiseAttentionSummaryChanged();
            }
            if (MissionActive && !_suppressedInterruptionKeys.Contains(key)) _focusFilteredCount++;
            if (_suppressedInterruptionKeys.Add(key))
                _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
                {
                    EventType = "InterruptionSuppressed", Category = source, Title = title,
                    Narrative = routing.Explanation, ExternalId = key, Outcome = "Suppressed", Significance = 20,
                    Metadata = JsonSerializer.Serialize(new { routing.PriorityScore, routing.ConfidencePercent, routing.Evidence })
                });
            return;
        }
        _suppressedInterruptionKeys.Remove(key);
        var existing = AttentionItems.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Source = source;
            existing.Severity = severity;
            existing.Title = title;
            existing.Detail = detail;
            existing.ActionKind = actionKind;
            existing.ContextId = contextId ?? "";
            existing.WebLink = webLink ?? "";
            existing.ExpiresAt = expiresAt;
            existing.DecisionOutcomeId = decisionOutcomeId ?? existing.DecisionOutcomeId;
            existing.InterruptionRoute = routing.Route;
            existing.InterruptionPriorityScore = routing.PriorityScore;
            existing.InterruptionConfidence = routing.ConfidencePercent;
            existing.InterruptionReason = routing.Explanation;
            existing.InterruptionEvidence = [..routing.Evidence];
            if (routing.Route == GuardianInterruptionRoute.Immediate) existing.InterruptionReleasedAt = now;
            else if (routing.Route == GuardianInterruptionRoute.Briefing) existing.InterruptionReleasedAt = null;
            SaveAttentionItems();
            RaiseAttentionSummaryChanged();
            return;
        }

        var item = new GuardianAttentionItem
        {
            Key = key,
            Source = source,
            Severity = severity,
            Title = title,
            Detail = detail,
            ActionKind = actionKind,
            ContextId = contextId ?? "",
            WebLink = webLink ?? "",
            ExpiresAt = expiresAt,
            DecisionOutcomeId = decisionOutcomeId,
            InterruptionRoute = routing.Route,
            InterruptionPriorityScore = routing.PriorityScore,
            InterruptionConfidence = routing.ConfidencePercent,
            InterruptionReason = routing.Explanation,
            InterruptionEvidence = [..routing.Evidence],
            InterruptionReleasedAt = routing.Route == GuardianInterruptionRoute.Immediate ? now : null,
            CreatedAt = DateTimeOffset.Now
        };
        AttentionItems.Insert(0, item);
        if (item.InterruptionRoute == GuardianInterruptionRoute.Immediate) RememberInterruption(item);
        _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "AttentionRequired", Category = source, Title = "Attention required",
            Narrative = $"{title}. {detail} Route: {routing.Route}. {routing.Explanation}", ExternalId = contextId ?? key,
            Significance = severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ? 90 : 65,
            Metadata = JsonSerializer.Serialize(new { routing.Route, routing.PriorityScore, routing.ConfidencePercent, routing.Evidence })
        });
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
        if (item.InterruptionRoute == GuardianInterruptionRoute.Immediate && IsUrgentAttention(item)) NotifyAttention();
    }

    private void ResolveAttention(string key)
    {
        var item = AttentionItems.FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (item is null || item.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase)) return;
        item.Status = "Resolved";
        item.SnoozedUntil = null;
        item.ResolvedAt = DateTimeOffset.Now;
        RememberResolvedAttention(item.Key);
        RecordAttentionResolution(item);
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private void ResolveAttentionByPrefix(string prefix, string? exceptKey = null)
    {
        var changed = false;
        foreach (var item in AttentionItems.Where(item =>
                     item.IsActive &&
                     item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                     !item.Key.Equals(exceptKey, StringComparison.OrdinalIgnoreCase)))
        {
            item.Status = "Resolved";
            item.SnoozedUntil = null;
            item.ResolvedAt = DateTimeOffset.Now;
            RememberResolvedAttention(item.Key);
            RecordAttentionResolution(item);
            changed = true;
        }

        if (!changed)
            return;

        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private void ClearTransientAttentionByPrefix(string prefix)
    {
        var removed = AttentionItems.Where(item => item.IsActive && item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (removed.Count == 0) return;
        foreach (var item in removed)
        {
            AttentionItems.Remove(item);
            _ = RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "ForecastPressureCleared", Category = item.Source, Title = item.Title,
                Narrative = "The live forecast returned within the actionable threshold; the transient warning was cleared.",
                ExternalId = item.Key, Outcome = "Cleared", Significance = 40
            });
        }
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
    }

    private static int AttentionSeverityRank(string severity) =>
        severity.ToLowerInvariant() switch
        {
            "critical" => 0,
            "high" => 1,
            "warning" => 2,
            _ => 3
        };
    private void WakeSnoozedAttentionItems()
    {
        var now = DateTimeOffset.Now;
        var changed = false;
        var urgentWoke = false;
        foreach (var item in AttentionItems.Where(item =>
                     item.Status.Equals("Snoozed", StringComparison.OrdinalIgnoreCase) &&
                     item.SnoozedUntil <= now))
        {
            item.Status = "Active";
            item.SnoozedUntil = null;
            changed = true;
            urgentWoke |= IsUrgentAttention(item);
        }
        if (!changed) return;
        SaveAttentionItems();
        RaiseAttentionSummaryChanged();
        if (urgentWoke) NotifyAttention();
    }

    private void NotifyAttention()
    {
        if (!VisibleAttentionItems.Any(x => x.Severity is "High" or "Critical") &&
            !CanPresentRoutinePrompt(Nekomata.Core.Guardian.Anticipation.GuardianPromptKind.Chases)) return;
        System.Media.SystemSounds.Exclamation.Play();
        var window = Application.Current?.MainWindow;
        if (window is null || window.WindowState != WindowState.Minimized) return;
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = new WindowInteropHelper(window).Handle,
            Flags = 3 | 12,
            Count = 5,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    private void LoadAttentionItems()
    {
        try
        {
            LoadResolvedAttentionKeys();
            if (!File.Exists(AttentionStorePath)) return;
            var items = JsonSerializer.Deserialize<List<GuardianAttentionItem>>(File.ReadAllText(AttentionStorePath)) ?? [];
            foreach (var item in items.OrderByDescending(item => item.CreatedAt).Take(100))
            {
                if ((item.ExpiresAt is not null && item.ExpiresAt <= DateTimeOffset.Now) ||
                    (item.Key.StartsWith("calendar:", StringComparison.OrdinalIgnoreCase) && item.CreatedAt < DateTimeOffset.Now.AddHours(-12)))
                {
                    item.Status = "Resolved";
                    item.ResolvedAt ??= DateTimeOffset.Now;
                    RememberResolvedAttention(item.Key);
                }
                AttentionItems.Add(item);
            }
            WakeSnoozedAttentionItems();
            RaiseAttentionSummaryChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Could not load Guardian attention history: " + ex);
        }
    }

    private void SaveAttentionItems()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AttentionStorePath)!);
            var retained = AttentionItems
                .Where(item => item.IsActive || item.Status == "Snoozed" || item.CreatedAt > DateTimeOffset.Now.AddDays(-14))
                .OrderByDescending(item => item.CreatedAt)
                .Take(100)
                .ToList();
            File.WriteAllText(AttentionStorePath, JsonSerializer.Serialize(retained));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Could not save Guardian attention history: " + ex);
        }
    }

    private void RaiseAttentionSummaryChanged()
    {
        OnPropertyChanged(nameof(VisibleAttentionItems));
        OnPropertyChanged(nameof(ActiveAttentionCount));
        OnPropertyChanged(nameof(HasActiveAttention));
        OnPropertyChanged(nameof(HasAttentionHistory));
        OnPropertyChanged(nameof(AttentionButtonLabel));
        OnPropertyChanged(nameof(AttentionSummaryLine));
        RefreshAmbientSituation();
    }

    private static string AttentionStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nekomata Personal",
        "attention-items.json");

    private static string ResolvedAttentionStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "resolved-attention-keys.json");

    private void RememberResolvedAttention(string key)
    {
        _resolvedAttentionKeys[key] = DateTimeOffset.Now;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResolvedAttentionStorePath)!);
            var retained = _resolvedAttentionKeys.Where(x => x.Value > DateTimeOffset.Now.AddDays(-30))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(ResolvedAttentionStorePath, JsonSerializer.Serialize(retained));
        }
        catch (Exception ex) { Debug.WriteLine("Could not persist resolved attention evidence: " + ex); }
    }

    private void LoadResolvedAttentionKeys()
    {
        try
        {
            if (!File.Exists(ResolvedAttentionStorePath)) return;
            _resolvedAttentionKeys = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(
                File.ReadAllText(ResolvedAttentionStorePath)) ?? new(StringComparer.OrdinalIgnoreCase);
            _resolvedAttentionKeys = _resolvedAttentionKeys.Where(x => x.Value > DateTimeOffset.Now.AddDays(-30))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Debug.WriteLine("Could not load resolved attention evidence: " + ex); }
    }

    private void RecordAttentionResolution(GuardianAttentionItem item) => _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
    {
        EventType = "AttentionResolved", Category = item.Source, Title = "Attention resolved",
        Narrative = $"{item.Title}. {item.CompletionEvidence} Guardian retained resolution evidence so this attention will not reappear on restart.",
        ExternalId = string.IsNullOrWhiteSpace(item.ContextId) ? item.Key : item.ContextId,
        Outcome = item.CompletionEvidence.StartsWith("Dismissed", StringComparison.OrdinalIgnoreCase) ? "Dismissed" : "Resolved",
        Significance = 45,
        Metadata = JsonSerializer.Serialize(new
        {
            item.InterruptionRoute, item.InterruptionPriorityScore, item.InterruptionConfidence,
            item.InterruptionReleasedAt, item.CompletionEvidence
        })
    });

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);
}
