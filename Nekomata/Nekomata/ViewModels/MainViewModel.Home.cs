using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Neglect;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.ViewModels;

public sealed record HomeTimelineItem(string Title, DateTime Start, DateTime End, string Kind, string Id = "")
{
    public string TimeLabel => $"{Start:HH:mm}–{End:HH:mm}";
}

public sealed record HomeNeedItem(string Title, string Why, string ActionLabel, Brush Dot, ICommand Act);
public sealed record HomeDoneItem(DateTime At, string Text) { public string Time => At.ToString("HH:mm"); }
public sealed record HomeServiceItem(string Name, string Detail, Brush Dot);
public sealed record HomeProjectItem(string Name, string Flag, Brush FlagBrush);

// The home page: one screen that answers, in order, what to do now, what the day looks like, what needs you,
// what Guardian has done for you, and how things look (connections, projects, commitments and the week ahead). Everything here is read from data the app already holds; it refreshes every 30 seconds.
// "Classic dashboard" keeps the old view one click away (remembered).
public partial class MainViewModel
{
    private static readonly Brush CriticalBrush = Frozen("#FF5A72");
    private static readonly Brush WarningBrush = Frozen("#FFC857");
    private static readonly Brush OkBrush = Frozen("#46F0B4");
    private static readonly Brush PurpleBrush = Frozen("#9C85FF");
    private static readonly Brush CyanBrush = Frozen("#42D9FF");
    private static readonly Brush MutedBrush = Frozen("#8FA4B8");

    private readonly DispatcherTimer _homeTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<HomeDoneItem> _doneToday = [];
    private IReadOnlyList<CalendarEvent> _homeTomorrow = [];
    private DateTimeOffset _homeTomorrowLoaded = DateTimeOffset.MinValue;
    private IReadOnlyList<NeglectItem> _homeSlipping = [];
    private DateTimeOffset _homeSlippingLoaded = DateTimeOffset.MinValue;
    private string _homeNowKind = "";

    [ObservableProperty] private string homeNowLabel = "NOW";
    [ObservableProperty] private string homeNowTitle = "Getting your day together…";
    [ObservableProperty] private string homeNowDetail = "";
    [ObservableProperty] private double homeNowProgress;
    [ObservableProperty] private bool hasHomeNowProgress;
    [ObservableProperty] private string homeNowPrimaryLabel = "Plan my day";
    [ObservableProperty] private bool homeNowCanPush;
    [ObservableProperty] private string homeNowNext = "";
    [ObservableProperty] private string homeTimelineSummary = "";
    [ObservableProperty] private bool homeOnPlan = true;
    /// <summary>How many things Guardian has done on its own today; the core sends out a ripple each time it rises.</summary>
    [ObservableProperty] private int homeDoneTotal;
    [ObservableProperty] private string homePlanLabel = "";
    [ObservableProperty] private DateTime homeDayStart = DateTime.Today.AddHours(8);
    [ObservableProperty] private DateTime homeDayEnd = DateTime.Today.AddHours(17.5);
    [ObservableProperty] private DateTime homeNow = DateTime.Now;
    [ObservableProperty] private string homeNeedsTitle = "NEEDS YOU";
    [ObservableProperty] private string homeDoneTitle = "DONE FOR YOU TODAY";
    [ObservableProperty] private string homeServicesSummary = "Checking…";
    [ObservableProperty] private string homeProjectsTitle = "Projects";
    [ObservableProperty] private string homeSlippingText = "";
    [ObservableProperty] private int homeHandedOver;

    public ObservableCollection<HomeTimelineItem> HomeTimeline { get; } = [];
    public ObservableCollection<HomeNeedItem> HomeNeeds { get; } = [];
    public ObservableCollection<HomeDoneItem> HomeDone { get; } = [];
    public ObservableCollection<HomeServiceItem> HomeServices { get; } = [];
    public ObservableCollection<HomeProjectItem> HomeProjects { get; } = [];



    /// <summary>The HUD home page is showing (the only home page now).</summary>
    public bool ShowModernHome => WorkspaceMode == WorkspaceMode.Dashboard;

    /// <summary>The HUD pages (home and battle mode) draw their own top line, so the shared header is hidden.</summary>
    public bool ShowHudChrome => WorkspaceMode is WorkspaceMode.Dashboard or WorkspaceMode.Mission;

    private void InitialiseHome()
    {
        LoadHomeUi();
        _homeTimer.Tick += (_, _) => _ = RefreshHomeAsync();
        _homeTimer.Start();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => _ = RefreshHomeAsync(), DispatcherPriority.ApplicationIdle);
    }

    partial void OnWorkspaceModeChanged(WorkspaceMode value)
    {
        OnPropertyChanged(nameof(ShowModernHome));
        OnPropertyChanged(nameof(ShowHudChrome));
        if (value == WorkspaceMode.Dashboard) _ = RefreshHomeAsync();
        // Other pages open with the conversation folded away, unless something was said in the last couple of minutes.
        else if (GuardianPanelExpanded && !_conversationActive && _promiseReview is null &&
                 ChatHistory.Count == _homeChatCount && DateTimeOffset.Now - _homeChatChangedAt > TimeSpan.FromMinutes(2))
            GuardianPanelExpanded = false;
    }

    [RelayCommand] private void ToggleHomeConversation() => GuardianPanelExpanded = !GuardianPanelExpanded;

    /// <summary>Something Guardian did on its own, for the home page's "Done for you today".</summary>
    private void RecordDone(string text)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => RecordDone(text)); return; }
        _doneToday.RemoveAll(d => d.At.Date != DateTime.Today);
        _doneToday.Insert(0, new HomeDoneItem(DateTime.Now, text));
        if (_doneToday.Count > 40) _doneToday.RemoveAt(_doneToday.Count - 1);
        RefreshHomeDone();
    }

    private void RefreshHomeDone()
    {
        HomeDone.Clear();
        foreach (var item in _doneToday.Where(d => d.At.Date == DateTime.Today).Take(6)) HomeDone.Add(item);
        var count = _doneToday.Count(d => d.At.Date == DateTime.Today);
        HomeDoneTitle = count == 0 ? "DONE FOR YOU TODAY" : $"DONE FOR YOU TODAY · {count}";
        HomeDoneTotal = count;
    }

    private bool _homeRefreshing;

    private async Task RefreshHomeAsync()
    {
        if (_homeRefreshing || IsInitialLoading) return;
        _homeRefreshing = true;
        try
        {
            var now = DateTimeOffset.Now;
            if (_autopilotToday.Count == 0) _autopilotToday = await SafeAsync<IReadOnlyList<CalendarEvent>>(() => LoadAutopilotDayAsync(now), Array.Empty<CalendarEvent>());
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var dayStart = settings.GetStart(DateTime.Today);
            var dayEnd = settings.GetEnd(DateTime.Today);
            var events = _autopilotToday.Where(e => !e.IsAllDay).OrderBy(e => e.Start).ToList();
            if (events.Count > 0)
            {
                dayStart = new[] { dayStart, events.Min(e => e.Start.LocalDateTime) }.Min();
                dayEnd = new[] { dayEnd, events.Max(e => e.End.LocalDateTime) }.Max();
            }
            HomeDayStart = dayStart.AddMinutes(-dayStart.Minute % 30);
            HomeDayEnd = dayEnd;
            HomeNow = now.LocalDateTime;

            RefreshHomeTimeline(events, now);
            await RefreshHomeNowAsync(events, now, settings);
            RefreshHomeCore(events, now, settings);
            RefreshHomeNeeds();
            RefreshHomeDone();
            await EvaluateHomeMeetingAsync();
            TidyHomeConversation(now);
            await MaybeRunHandoffAsync(now, settings);
            MaybeSelfCheck(now);
            MaybeRefreshWeather(now);
            MaybeWeeklyFeedbackNote(now);
            _ = MaybeCaptureAsync(now);
            _ = MaybeRefreshPrivacyDirectoryAsync(now);
            MaybePromptAfterMeeting(now);
            MaybeCheckJobs(now);
            _ = MaybeCheckPlaceAsync(now);
            if (!OffHoursActive)
            {
                // Proactive work pauses while Guardian is off duty (see MainViewModel.OffHours).
                // Each can be switched off on the settings page.
                _ = MaybeWeekReviewAsync(now);
                if (FeatureOn("meeting-catchup")) MaybeCatchUpAfterMeeting(events, now);
                if (FeatureOn("break-catchup")) MaybeCatchUpAfterBreak(now);
                if (FeatureOn("invites")) _ = MaybeCheckInvitesAsync(now);
                if (FeatureOn("approvals")) _ = MaybeScanApprovalsAsync(now);
                if (FeatureOn("projects")) _ = MaybeRunProjectAutopilotAsync(now);
                if (FeatureOn("duties")) MaybeCheckDuties(now);
                if (FeatureOn("objectives")) MaybeLinkObjectives(now);
                if (FeatureOn("meeting-load")) _ = MaybeMeetingLoadAsync(now);
                _ = MaybeOfferSetupCheckAsync(now);
                MaybeTimeNotes(now);
            }
            if (_teamsLastPoll is null && now >= _teamsRetryAt) _ = RefreshTeamsAsync();
            RefreshHomeOperations();
            await RefreshHomeWeekAsync(now);
            RefreshHomeSignals();
            ApplyHomeMood();
            RunPersonalityBeats(events, now, settings);
            RecordHomeDay(events, now);
            await RefreshHomeWorkAsync(now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Home refresh failed safely: {ex.Message}");
        }
        finally
        {
            _homeRefreshing = false;
        }
    }

    private void RefreshHomeTimeline(List<CalendarEvent> events, DateTimeOffset now)
    {
        HomeTimeline.Clear();
        foreach (var e in events)
        {
            var kind = e.IsNekomataManaged ? "focus" : e.Attendees.Count > 0 ? "meeting" : "own";
            HomeTimeline.Add(new HomeTimelineItem(CleanCalendarObjectiveTitle(e.Subject), e.Start.LocalDateTime, e.End.LocalDateTime, kind, e.Id));
        }
        var focus = events.Where(e => e.IsNekomataManaged).Sum(e => (e.End - e.Start).TotalMinutes);
        var meetings = events.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0).Sum(e => (e.End - e.Start).TotalMinutes);
        var finish = events.Count == 0 ? (DateTime?)null : events.Max(e => e.End.LocalDateTime);
        HomeOnPlan = !HudOffPlan;
        HomePlanLabel = events.Count == 0 ? "Nothing booked" : HudOffPlan ? "Off plan" : "On plan";
        HomeTimelineSummary = events.Count == 0
            ? "Nothing in the calendar today. Say plan my day and Guardian will lay it out."
            : $"{Hours(focus)} focus booked · " + (meetings < 1 ? "no meetings" : $"{Hours(meetings)} in meetings") + (finish is { } f ? $" · finish around {f:HH:mm}" : "") +
              (AutopilotEnabled ? ". Guardian moves its own blocks if a meeting lands on them." : ".");

        static string Hours(double minutes) => minutes < 60 ? $"{minutes:0} min" : $"{Math.Floor(minutes / 60):0} h{(minutes % 60 >= 1 ? $" {minutes % 60:0}" : "")}";
    }

    private async Task RefreshHomeNowAsync(List<CalendarEvent> events, DateTimeOffset now, WorkingDaySettings settings)
    {
        var running = RunningGuardianBlock(now);
        var meeting = events.FirstOrDefault(e => !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start <= now && e.End > now);
        var next = events.FirstOrDefault(e => !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start > now);
        HomeNowNext = next is null ? "" : $"Next: {CleanCalendarObjectiveTitle(next.Subject)} at {next.Start.LocalDateTime:HH:mm}" +
                                          (GuardianVoiceEnabled ? $" · brief at {next.Start.LocalDateTime.AddMinutes(-5):HH:mm}" : "");
        HomeNowCanPush = false;
        HasHomeNowProgress = false;

        if (MissionActive && Workspace.CurrentMission is { } mission)
        {
            _homeNowKind = "mission";
            HomeNowLabel = "IN FOCUS";
            HomeNowTitle = mission.Title;
            HomeNowDetail = $"{MissionRemaining} left" + (string.IsNullOrWhiteSpace(mission.RecommendationReason) ? "" : $" · {mission.RecommendationReason}");
            HomeNowPrimaryLabel = "Open focus";
            return;
        }
        if (running is not null)
        {
            _homeNowKind = "block";
            var total = (running.End - running.Start).TotalMinutes;
            var left = Math.Max(0, (int)Math.Ceiling((running.End - now).TotalMinutes));
            HomeNowLabel = "NOW · FOCUS BLOCK";
            HomeNowTitle = CleanCalendarObjectiveTitle(running.Subject);
            HomeNowDetail = $"Until {running.End.LocalDateTime:HH:mm} · {left} minutes left";
            HomeNowProgress = total <= 0 ? 0 : Math.Clamp((now - running.Start).TotalMinutes / total * 100, 0, 100);
            HasHomeNowProgress = true;
            HomeNowPrimaryLabel = "Start focus";
            HomeNowCanPush = true;
            return;
        }
        if (meeting is not null)
        {
            _homeNowKind = "meeting";
            HomeNowLabel = "NOW · MEETING";
            HomeNowTitle = CleanCalendarObjectiveTitle(meeting.Subject);
            HomeNowDetail = $"Until {meeting.End.LocalDateTime:HH:mm} · say note that… to capture anything for the follow-up";
            HomeNowProgress = Math.Clamp((now - meeting.Start).TotalMinutes / Math.Max(1, (meeting.End - meeting.Start).TotalMinutes) * 100, 0, 100);
            HasHomeNowProgress = true;
            HomeNowPrimaryLabel = "Brief me";
            return;
        }
        if (now.LocalDateTime >= settings.GetEnd(now.Date) || now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            _homeNowKind = "evening";
            if (now - _homeTomorrowLoaded > TimeSpan.FromMinutes(10))
            {
                var next0 = Nekomata.Core.Guardian.Outcomes.WrapUpPlanning.NextWorkday(now.Date);
                var start = new DateTimeOffset(next0, TimeZoneInfo.Local.GetUtcOffset(next0));
                _homeTomorrow = await SafeAsync<IReadOnlyList<CalendarEvent>>(() =>
                    _services.GetRequiredService<ICalendarService>().GetEventsAsync(start, start.AddDays(1)), Array.Empty<CalendarEvent>());
                _homeTomorrowLoaded = now;
            }
            var first = _homeTomorrow.Where(e => !e.IsAllDay).OrderBy(e => e.Start).FirstOrDefault();
            HomeNowLabel = "FIRST THING TOMORROW";
            HomeNowTitle = first is null ? "Nothing booked yet" : CleanCalendarObjectiveTitle(first.Subject);
            HomeNowDetail = first is null ? "Say plan tomorrow and Guardian will lay out the day."
                : $"{first.Start.LocalDateTime:dddd HH:mm}–{first.End.LocalDateTime:HH:mm}" +
                  (_homeTomorrow.Count(e => !e.IsAllDay) > 1 ? $" · {_homeTomorrow.Count(e => !e.IsAllDay) - 1} more after that" : "");
            HomeNowPrimaryLabel = "Plan tomorrow";
            HomeNowNext = "";
            return;
        }
        _homeNowKind = "free";
        var candidate = Workspace.CurrentMission;
        HomeNowLabel = "NOW · FREE TIME";
        HomeNowTitle = candidate is null || string.IsNullOrWhiteSpace(candidate.Title) ? "Nothing scheduled right now" : candidate.Title;
        var freeUntil = events.Where(e => e.Start > now).Select(e => e.Start.LocalDateTime).DefaultIfEmpty(settings.GetEnd(now.Date)).Min();
        HomeNowDetail = $"Free until {freeUntil:HH:mm}" + (candidate is null || string.IsNullOrWhiteSpace(candidate.RecommendationReason) ? "" : $" · {candidate.RecommendationReason}");
        HomeNowPrimaryLabel = candidate is null || string.IsNullOrWhiteSpace(candidate.Title) ? "Plan my day" : "Start this";
    }

    [RelayCommand]
    private async Task HomePrimaryAsync()
    {
        switch (_homeNowKind)
        {
            case "mission": ReturnToActiveMission(); break;
            case "block": await StartCurrentBlockAsync(); break;
            case "meeting": await RunHomeCommandAsync("brief me"); break;
            case "evening": await RunHomeCommandAsync("plan tomorrow"); break;
            default:
                if (Workspace.CurrentMission is { Title.Length: > 0 }) await BeginMissionAsync();
                else await RunHomeCommandAsync("plan my day");
                break;
        }
        await RefreshHomeAsync();
    }

    [RelayCommand] private async Task HomePushAsync() { await PushCurrentBlockAsync(); await RefreshHomeAsync(); }

    [RelayCommand] private Task HomeWhyAsync() => RunHomeCommandAsync($"Why is {HomeNowTitle} the right thing to be doing now?");

    [RelayCommand] private Task HomeQuickAskAsync(string? text) => string.IsNullOrWhiteSpace(text) ? Task.CompletedTask : RunHomeCommandAsync(text);

    private async Task RunHomeCommandAsync(string text)
    {
        CommandPaletteInput = text;
        await ExecuteCommandPaletteAsync();
    }

    private void RefreshHomeNeeds()
    {
        HomeNeeds.Clear();
        var now = DateTimeOffset.Now;
        if (_pendingAutonomy is { } pending && pending.Expires > now)
            HomeNeeds.Add(new HomeNeedItem($"Go-ahead needed to {pending.Description}", "Guardian proposed it and is waiting for you",
                "Do it", CyanBrush, new AsyncRelayCommand(() => ConfirmPendingAutonomyAsync("do it"))));

        var extras = HomeNeeds.Count;
        var items = VisibleAttentionItems
            .OrderBy(a => a.Severity switch { "Critical" => 0, "Warning" => 1, _ => 2 })
            .ThenByDescending(a => a.CreatedAt)
            .ToList();
        foreach (var item in items.Take(6 - HomeNeeds.Count))
        {
            var dot = item.Severity switch { "Critical" => CriticalBrush, "Warning" => WarningBrush, _ => PurpleBrush };
            HomeNeeds.Add(new HomeNeedItem(item.Title, string.IsNullOrWhiteSpace(item.Detail) ? item.Source : item.Detail,
                SentenceCase(item.ActionLabel), dot, new AsyncRelayCommand(() => ActAttentionItemAsync(item))));
        }
        var total = items.Count + extras;
        HomeNeedsTitle = total == 0 ? "NEEDS YOU · NOTHING RIGHT NOW" : $"NEEDS YOU · {total}";

        static string SentenceCase(string text)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return "Open";
            t = t.ToLowerInvariant();
            t = char.ToUpperInvariant(t[0]) + t[1..];
            return t.Length > 22 ? t[..22].TrimEnd() + "…" : t;
        }
    }

    private void RefreshHomeOperations()
    {
        HomeServices.Clear();
        foreach (var d in Diagnostics.Take(8))
            HomeServices.Add(new HomeServiceItem(d.Name, d.Summary, d.IsHealthy ? OkBrush : d.NeedsAttention ? WarningBrush : MutedBrush));
        var transcripts = AutonomyRows.FirstOrDefault(r => r.Action == Nekomata.Core.Guardian.Autonomy.AutonomyAction.MeetingFollowThrough);
        if (transcripts is { NeedsAttention: true })
            HomeServices.Add(new HomeServiceItem("Teams transcripts", "Needs permission", WarningBrush));
        HomeServicesSummary = Diagnostics.Count == 0 ? "Checking…"
            : AttentionDiagnosticCount == 0 ? $"All healthy · checked {Diagnostics.Max(d => d.CheckedAt):HH:mm}"
            : $"{AttentionDiagnosticCount} need attention";
    }

    private async Task RefreshHomeWorkAsync(DateTimeOffset now)
    {
        var active = Workspace.Projects.Where(p => p.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)).ToList();
        HomeProjectsTitle = $"PROJECTS · {active.Count} ACTIVE";
        HomeProjects.Clear();
        var flagged = active.Select(p =>
            {
                var idle = (DateTime.Now - p.UpdatedAt).Days;
                return p.AtRisk ? (p, Flag: "At risk", Brush: CriticalBrush, Rank: 0)
                    : idle >= 14 ? (p, Flag: $"Untouched {idle} days", Brush: WarningBrush, Rank: 1)
                    : p.Priority.Equals("High", StringComparison.OrdinalIgnoreCase) ? (p, Flag: "High priority", Brush: PurpleBrush, Rank: 3)
                    : (p, Flag: "On track", Brush: OkBrush, Rank: 4);
            })
            .OrderBy(x => x.Rank).ThenBy(x => x.p.Name).Take(5);
        foreach (var f in flagged) HomeProjects.Add(new HomeProjectItem(f.p.Name, f.Flag, f.Brush));

        HomeHandedOver = _delegations.Count(d => d.Status != "Done");
        if (now - _homeSlippingLoaded > TimeSpan.FromMinutes(30))
        {
            _homeSlippingLoaded = now;
            _homeSlipping = await SafeAsync(DetectNeglectAsync, (IReadOnlyList<NeglectItem>)Array.Empty<NeglectItem>());
        }
        RefreshHomeSlipping();
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
