using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Core.Analytics.Capacity;
using Nekomata.Core.Missions;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Workspace;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;
using Nekomata.UI.Services;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Core.Guardian.Context;
using Nekomata.Models.Guardian;
using Nekomata.Data.Repositories;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Diagnostics;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private DateTime selectedCalendarDate = DateTime.Today;
    [ObservableProperty] private ObservableCollection<CalendarEvent> calendarEvents = [];
    [ObservableProperty] private bool calendarBusy;
    [ObservableProperty] private bool calendarLoaded;
    [ObservableProperty] private string calendarStatus = "Connect your Microsoft calendar to see the shape of your day.";
    [ObservableProperty] private bool calendarScheduling;
    [ObservableProperty] private string calendarSchedulingStatus = "Guardian can place ranked work into free focus blocks.";
    [ObservableProperty] private string nowTimelineText = "Loading calendar…";
    [ObservableProperty] private string nextTimelineText = "—";
    [ObservableProperty] private string thenTimelineText = "—";
    [ObservableProperty] private string calendarLastSyncedText = "Calendar not synced";
    [ObservableProperty] private string capacityBreakdownText = "Calendar capacity not loaded";
    [ObservableProperty] private GuardianWorkThreadSummary? threadContextDrawer;
    [ObservableProperty] private bool threadContextDrawerVisible;
    [ObservableProperty] private bool threadContextResolverVisible;
    [ObservableProperty] private string threadContextResolverTitle = "Select a Work Thread";
    [ObservableProperty] private string threadContextResolverDetail = "Guardian could not confirm this relationship automatically.";
    [ObservableProperty] private ObservableCollection<GuardianWorkThreadSummary> threadContextResolverOptions = [];
    [ObservableProperty] private ObservableCollection<Nekomata.Models.Tasks.NekomataTask> threadContextTaskOptions = [];
    public bool HasThreadContextTaskOptions => ThreadContextTaskOptions.Count > 0;
    private CalendarEvent? _threadContextResolverEvent;
    private CalendarEvent? _threadContextDrawerEvent;
    private DateTime _calendarCapacitySnapshotDate;
    private bool _hasCalendarCapacitySnapshot;
    private IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> _cachedCalendarCapacityIntervals = [];
    private int _cachedScheduledFocusMinutesToday;
    private readonly DispatcherTimer _calendarBoundaryTimer = new();
    private CalendarTimelineContext _calendarContext = CalendarTimelineContext.Empty;
    private CalendarEvent? _nowTimelineEvent => _calendarContext.Active;
    private CalendarEvent? _nextTimelineEvent => _calendarContext.Next;
    private CalendarEvent? _thenTimelineEvent => _calendarContext.Then;

    public string NowTimelineActionText => _calendarContext.HasActiveFocus ? "START" : "OPEN";
    public bool HasNowTimelineAction => _nowTimelineEvent is not null;
    public bool HasNextTimelineAction => _nextTimelineEvent is not null;
    public bool HasThenTimelineAction => _thenTimelineEvent is not null;

    [ObservableProperty] private bool planAttentionVisible;
    [ObservableProperty] private string planHealthTitle = "PLAN HEALTH · Monitoring";
    [ObservableProperty] private string planHealthDetail = "Guardian is watching today’s calendar plan.";
    [ObservableProperty] private bool canStartPlanAlert;
    private readonly DispatcherTimer _planHealthTimer = new();
    private CalendarEvent? _planAlertEvent;
    private string? _dismissedPlanAlertKey;
    private string? _lastAlertSoundKey;
    private string? _unworkedBlockKey;
    private DateTimeOffset? _unworkedBlockEnd;

    public bool HasCalendarEvents => CalendarEvents.Count > 0;
    public bool ShowCalendarEmptyState => CalendarLoaded && !CalendarBusy && !HasCalendarEvents;
    public string CalendarDateLabel => SelectedCalendarDate.ToString("dddd, d MMMM");
    public string CalendarSummary => HasCalendarEvents
        ? $"{CalendarEvents.Count} event{(CalendarEvents.Count == 1 ? string.Empty : "s")} · {CalendarEvents.Where(x => !x.IsAllDay).Sum(x => Math.Max(0, (int)(x.End - x.Start).TotalMinutes)) / 60d:0.#} hours booked"
        : "Your day is open";
    public bool CanUndoCalendarPlan => _services.GetRequiredService<CalendarUndoService>().CanUndo;

    [ObservableProperty]
    private bool calendarObjectiveAvailable;
    [ObservableProperty]
    private bool canBeginBriefingObjective = true;
    private readonly SemaphoreSlim _calendarObjectiveRefreshLock = new(1, 1);

    private async Task RefreshCalendarAwareObjectiveAsync()
    {
        await _calendarObjectiveRefreshLock.WaitAsync();
        try { await RefreshCalendarAwareObjectiveCoreAsync(); }
        finally { _calendarObjectiveRefreshLock.Release(); }
    }

    private async Task RefreshCalendarAwareObjectiveCoreAsync()
    {
        if (MissionActive)
        {
            try
            {
                var calendar = _services.GetRequiredService<ICalendarService>();
                var today = DateTime.Today;
                var offset = TimeZoneInfo.Local.GetUtcOffset(today);
                var start = new DateTimeOffset(today, offset);
                var events = await calendar.GetEventsAsync(start, start.AddDays(1));
                var now = DateTimeOffset.Now;
                UpdateLiveTimeline(events, now);
                ScheduleNextCalendarBoundary(events, now);
                CalendarLastSyncedText = $"Calendar synced {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Active-mission timeline refresh failed: {ex}");
            }
            SyncBriefingObjective(Workspace.CurrentMission);
            CalendarObjectiveAvailable = true;
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var offset = TimeZoneInfo.Local.GetUtcOffset(now.Date);
            var workStart = new DateTimeOffset(settings.GetStart(now.Date), offset);
            var workEnd = new DateTimeOffset(settings.GetEnd(now.Date), offset);
            var service = _services.GetRequiredService<ICalendarService>();
            // An active calendar commitment remains execution truth even when it starts
            // before configured work hours or runs beyond them.
            var dayStart = new DateTimeOffset(now.Date, offset);
            var events = (await service.GetEventsAsync(dayStart, dayStart.AddDays(1))).OrderBy(item => item.Start).ToList();
            UpdateLiveTimeline(events, now);
            ScheduleNextCalendarBoundary(events, now);
            var activeEvent = events.FirstOrDefault(item => item.Start <= now && item.End > now);
            if (CalendarTimelineContextResolver.ShouldSuppressObjectiveOutsideWorkPeriod(
                now, workStart, workEnd, activeEvent is not null))
            {
                ShowTopRankedObjective("This is your highest-ranked available task. It is ready for the next working window.");
                return;
            }
            MissionCandidate? candidate = null;
            var freeMinutes = 0;

            if (activeEvent is null && _manualObjectiveOverride is not null)
            {
                if (IsPromotedObjectiveStillAvailable(_manualObjectiveOverride, Workspace))
                {
                    Workspace.CurrentMission = _manualObjectiveOverride;
                    SyncBriefingObjective(_manualObjectiveOverride);
                    CalendarObjectiveAvailable = true;
                    OnPropertyChanged(nameof(Workspace));
                    return;
                }

                _manualObjectiveOverride = null;
            }

            if (IsPlannedFocusBlock(activeEvent))
            {
                // The visible calendar subject is execution truth. Body markers may be
                // stale after a block is renamed or repurposed.
                candidate = Workspace.RankedMissionCandidates.FirstOrDefault(item =>
                    CalendarTitlesMatch(activeEvent!.Subject, item.Title) ||
                    (!string.IsNullOrWhiteSpace(item.SourceRecordId) &&
                     activeEvent.Subject.Contains($"#{item.SourceRecordId}", StringComparison.OrdinalIgnoreCase)));
            }
            else if (activeEvent is null)
            {
                var freeEnd = events.Where(item => item.Start > now).Select(item => item.Start).DefaultIfEmpty(workEnd).Min();
                freeMinutes = Math.Max(0, (int)(freeEnd - now).TotalMinutes);
                candidate = freeMinutes <= 0 ? null : GetBestFreeWindowCandidate(freeMinutes);
            }

            if (candidate is null && IsPlannedFocusBlock(activeEvent))
            {
                Workspace.CurrentMission = new Mission
                {
                    TaskId = ResolveCalendarTaskId(activeEvent),
                    SourceType = "Calendar",
                    SourceRecordId = activeEvent!.Id,
                    Title = CleanCalendarObjectiveTitle(activeEvent!.Subject),
                    Description = "Live Guardian calendar block",
                    EstimatedDuration = activeEvent.End - now,
                    StartBefore = activeEvent.Start.LocalDateTime,
                    Status = "SCHEDULED NOW",
                    ThreatLevel = "LOW",
                    RecommendationReason = "This is the active item in Guardian's calendar plan."
                };
            }
            else if (candidate is not null)
            {
                Workspace.CurrentMission = new Mission
                {
                    TaskId = candidate.TaskId,
                    ProjectId = candidate.ProjectId,
                    SourceType = candidate.SourceType,
                    SourceRecordId = candidate.SourceRecordId,
                    Title = IsPlannedFocusBlock(activeEvent)
                        ? CleanCalendarObjectiveTitle(activeEvent!.Subject)
                        : candidate.Title,
                    Description = candidate.Description,
                    Score = candidate.Score,
                    BusinessValue = candidate.BusinessValue,
                    EstimatedDuration = IsPlannedFocusBlock(activeEvent)
                        ? activeEvent!.End - now
                        : TimeSpan.FromMinutes(FreeWindowMissionPolicy.EffectiveMinutes(candidate)),
                    StartBefore = IsPlannedFocusBlock(activeEvent) ? activeEvent!.Start.LocalDateTime : now.LocalDateTime,
                    Status = IsPlannedFocusBlock(activeEvent) ? "SCHEDULED NOW" :
                        candidate.IsUrgent || candidate.RequiresImmediateAttention ? "IMMEDIATE ATTENTION" : "READY IN FREE WINDOW",
                    Progress = candidate.Progress,
                    ThreatLevel = candidate.AtRisk ? "HIGH" : candidate.Score >= 80 ? "HIGH" : candidate.Score >= 50 ? "MEDIUM" : "LOW",
                    RecommendationReason = IsPlannedFocusBlock(activeEvent)
                        ? "This task is the active item in Guardian's calendar plan."
                        : candidate.IsUrgent || candidate.RequiresImmediateAttention
                            ? "This immediate-attention item is exempt from normal free-window fitting."
                            : "This task fits the current free calendar window.",
                    ScoreFactors = candidate.ScoreFactors.ToList(),
                    Strengths = candidate.Strengths.ToList(),
                    Risks = candidate.Risks.ToList(),
                    GuardianReasons = candidate.GuardianReasons.ToList()
                };
            }
            else
            {
                if (activeEvent is not null)
                {
                    var nextEvent = events
                        .Where(item => item.Start >= activeEvent.End)
                        .OrderBy(item => item.Start)
                        .FirstOrDefault();
                    SetActiveMeetingContext(activeEvent, nextEvent);
                }
                else
                {
                    ClearCalendarObjective(freeMinutes);
                }
                return;
            }

            SyncBriefingObjective(Workspace.CurrentMission);
            CalendarObjectiveAvailable = true;
            OnPropertyChanged(nameof(Workspace));
        }
        catch (Exception ex)
        {
            ShowTopRankedObjective("This is your highest-ranked available task. Calendar availability could not be checked just now.");
            System.Diagnostics.Debug.WriteLine($"Calendar-aware objective refresh failed: {ex}");
        }
    }

    private MissionCandidate? GetBestFreeWindowCandidate(int availableMinutes)
    {
        var ranked = FreeWindowMissionPolicy.Select(Workspace.RankedMissionCandidates, availableMinutes);
        if (ranked is not null) return ranked;

        var task = Workspace.Tasks
            .Where(item => !item.IsCompleted && !item.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            .Where(item => Math.Max(1,
                (item.PredictedActiveMinutes > 0 ? item.PredictedActiveMinutes : item.EstimatedMinutes) - item.ActualMinutes) <= availableMinutes)
            .OrderBy(item => item.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(item => item.PriorityScore)
            .ThenByDescending(item => item.EstimatedBusinessValue)
            .FirstOrDefault();
        var fallback = task is null ? null : new MissionCandidate
        {
            SourceType = "Task", TaskId = task.Id, ProjectId = task.ProjectId,
            Title = task.Title, Description = task.Description ?? string.Empty,
            EstimatedMinutes = Math.Max(1, task.EstimatedMinutes - task.ActualMinutes),
            PredictedActiveMinutes = Math.Max(1,
                (task.PredictedActiveMinutes > 0 ? task.PredictedActiveMinutes : task.EstimatedMinutes) - task.ActualMinutes),
            EnteredEstimateMinutes = task.EstimatedMinutes,
            BusinessValue = task.EstimatedBusinessValue, Priority = task.Priority,
            DueAt = task.DueAt, Score = task.PriorityScore,
            RecommendationReason = "This is the strongest open task available in the current free calendar window."
        };
        return fallback;
    }

    private async Task FindAndPromoteNextObjectiveAsync()
    {
        if (MissionActive)
        {
            ShowPaletteResponse($"{Workspace.CurrentMission.Title} is already active. Complete, hold or discard it before Guardian promotes another objective.");
            return;
        }

        await RefreshCalendarAwareObjectiveAsync();
        var now = DateTimeOffset.Now;
        if (_nowTimelineEvent is not null && _nowTimelineEvent.Start <= now && _nowTimelineEvent.End > now)
        {
            if (IsPlannedFocusBlock(_nowTimelineEvent)) SetActiveFocusObjective(_nowTimelineEvent, now);
            ShowPaletteResponse($"Calendar priority: {CleanCalendarObjectiveTitle(_nowTimelineEvent.Subject)} until {_nowTimelineEvent.End:HH:mm}. Guardian has kept the scheduled block as your next objective.");
            return;
        }

        var availableMinutes = CurrentFreeCalendarMinutes();
        var candidate = GetBestFreeWindowCandidate(availableMinutes);
        if (candidate is null)
        {
            ClearCalendarObjective(availableMinutes);
            ShowPaletteResponse($"No ranked objective fits the current {availableMinutes}-minute calendar gap. Guardian has protected the next commitment and offered a short review/capture step instead.");
            return;
        }

        Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(candidate);
        Workspace.CurrentMission.Status = "PROMOTED NEXT";
        Workspace.CurrentMission.RecommendationReason = string.IsNullOrWhiteSpace(candidate.RecommendationReason)
            ? "This is the strongest actionable item available outside a scheduled calendar block."
            : candidate.RecommendationReason;
        SyncBriefingObjective(Workspace.CurrentMission);
        TrackSurfacedEntity("MissionCandidate", Nekomata.Core.Missions.NextActionPolicy.Key(candidate), candidate.Title, "FindNextThing");
        CalendarObjectiveAvailable = true;
        OnPropertyChanged(nameof(Workspace));
        ShowPaletteResponse($"Next objective promoted: {Workspace.CurrentMission.Title} · approximately {(int)Math.Ceiling(Workspace.CurrentMission.EstimatedDuration.TotalMinutes)} minutes.");
    }

    private void SyncBriefingObjective(Mission mission)
    {
        CanBeginBriefingObjective = true;
        var briefing = Workspace.Briefing;
        briefing.PrimaryFocus = mission.Title;
        briefing.ObjectiveTitle = mission.Title;
        briefing.ObjectiveReason = mission.RecommendationReason;
        briefing.ObjectiveScore = mission.Score;
        briefing.ObjectiveBusinessValue = mission.BusinessValue;
        briefing.ObjectiveEstimatedMinutes = Math.Max(0, (int)Math.Ceiling(mission.EstimatedDuration.TotalMinutes));
        briefing.ObjectiveStartBefore = mission.StartBefore;
        briefing.ObjectiveTaskId = mission.TaskId;
        briefing.ObjectiveProjectId = mission.ProjectId;
        briefing.Headline = mission.Status == "SCHEDULED NOW" ? $"Now: {mission.Title}." : $"Next available objective: {mission.Title}.";
        briefing.GuardianComment = mission.Status == "SCHEDULED NOW"
            ? $"Continue with '{mission.Title}' until the current calendar block ends."
            : mission.SourceType == "Guardian" || mission.Status is "IMMEDIATE ATTENTION" or "MANUALLY SELECTED"
                ? mission.RecommendationReason
                : $"'{mission.Title}' fits the current free calendar window.";
        ApplyGuardianCalendarGuidance();
        OnPropertyChanged(nameof(HasBriefingObjective));
    }

    private void SetActiveMeetingContext(CalendarEvent activeMeeting, CalendarEvent? nextEvent)
    {
        var meetingTitle = CleanCalendarObjectiveTitle(activeMeeting.Subject);
        CalendarObjectiveAvailable = false;
        CanBeginBriefingObjective = false;
        var briefing = Workspace.Briefing;
        briefing.PrimaryFocus = meetingTitle;
        briefing.ObjectiveTitle = meetingTitle;
        briefing.ObjectiveReason = $"Protected meeting in progress until {activeMeeting.End:HH:mm}. Stay with the meeting; capture tasks and follow-ups as they arise.";
        briefing.ObjectiveScore = 0;
        briefing.ObjectiveBusinessValue = 0;
        briefing.ObjectiveEstimatedMinutes = Math.Max(1, (int)Math.Ceiling((activeMeeting.End - DateTimeOffset.Now).TotalMinutes));
        briefing.ObjectiveStartBefore = activeMeeting.Start.LocalDateTime;
        briefing.ObjectiveTaskId = null;
        briefing.ObjectiveProjectId = null;
        Workspace.Briefing.Headline = $"You are currently in {meetingTitle}.";
        var calendarComment = nextEvent is null
            ? $"You are currently in {meetingTitle} until {activeMeeting.End:HH:mm}. No further block is scheduled today."
            : $"You are currently in {meetingTitle} until {activeMeeting.End:HH:mm}. " +
              $"Your next block is for {CleanCalendarObjectiveTitle(nextEvent.Subject)} at {nextEvent.Start:HH:mm}.";
        OnPropertyChanged(nameof(HasBriefingObjective));
        OnPropertyChanged(nameof(Workspace));
    }

    private bool ShowTopRankedObjective(string availabilityMessage)
    {
        var candidate = Workspace.RankedMissionCandidates
            .Where(item => item.IsActionable && !item.IsOnHold && !item.IsAwaitingExternalResponse)
            .OrderByDescending(item => item.RequiresImmediateAttention)
            .ThenBy(item => item.Rank)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();

        if (candidate is null)
        {
            ClearCalendarObjective();
            return false;
        }

        var mission = _services.GetRequiredService<IMissionFactory>().Create(candidate);
        mission.Status = "RANKED";
        Workspace.CurrentMission = mission;
        SyncBriefingObjective(mission);
        CalendarObjectiveAvailable = false;
        Workspace.Briefing.Headline = $"Next ranked objective: {mission.Title}.";
        Workspace.Briefing.GuardianComment = availabilityMessage;
        OnPropertyChanged(nameof(Workspace));
        return true;
    }

    private void ClearCalendarObjective(int availableMinutes = 0)
    {
        var now = DateTimeOffset.Now;
        if (IsPlannedFocusBlock(_nowTimelineEvent) &&
            _nowTimelineEvent!.Start <= now &&
            _nowTimelineEvent.End > now)
        {
            SetActiveFocusObjective(_nowTimelineEvent, now);
            return;
        }

        var fallback = availableMinutes >= 10 ? GetBestFreeWindowCandidate(availableMinutes) : null;
        Workspace.CurrentMission = fallback is null
            ? new Mission
            {
                SourceType = "Guardian", Title = "Review and capture next actions",
                Description = "Clear attention items, capture new work and choose the next concrete action.",
                EstimatedDuration = TimeSpan.FromMinutes(availableMinutes >= 5 ? Math.Min(15, availableMinutes) : 15), Status = "READY", ThreatLevel = "LOW",
                RecommendationReason = availableMinutes >= 5
                    ? $"No ranked objective fits the current {availableMinutes}-minute gap; use it for a short review without overrunning the next commitment."
                    : "No safe calendar window is currently available for ranked work."
            }
            : _services.GetRequiredService<IMissionFactory>().Create(fallback);
        if (fallback is not null)
        {
            Workspace.CurrentMission.Status = fallback.IsUrgent || fallback.RequiresImmediateAttention
                ? "IMMEDIATE ATTENTION"
                : "READY IN FREE WINDOW";
            Workspace.CurrentMission.RecommendationReason = fallback.IsUrgent || fallback.RequiresImmediateAttention
                ? "This immediate-attention item is exempt from normal free-window fitting."
                : "This task fits the current free calendar window.";
        }
        SyncBriefingObjective(Workspace.CurrentMission);
        if (fallback is null && availableMinutes < 5)
        {
            CanBeginBriefingObjective = false;
            OnPropertyChanged(nameof(CanBeginBriefingObjective));
        }
        CalendarObjectiveAvailable = true;
        OnPropertyChanged(nameof(Workspace));
    }

    private int CurrentFreeCalendarMinutes()
    {
        var now = DateTimeOffset.Now;
        var local = now.LocalDateTime;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            local < settings.GetStart(local) || local >= settings.GetEnd(local)) return 0;
        if (CalendarEvents.Any(item => item.Start <= now && item.End > now)) return 0;
        var boundary = new DateTimeOffset(settings.GetEnd(local));
        foreach (var entry in CalendarEvents.Where(item => item.Start > now && item.Start < boundary)) boundary = entry.Start;
        if (settings.IncludeLunchBreak)
        {
            if (local >= settings.GetLunchStart(local) && local < settings.GetLunchEnd(local)) return 0;
            var lunch = new DateTimeOffset(settings.GetLunchStart(local));
            if (lunch > now && lunch < boundary) boundary = lunch;
        }
        return Math.Max(0, (int)Math.Floor((boundary - now).TotalMinutes));
    }

    private void SetActiveFocusObjective(
        CalendarEvent activeEvent,
        DateTimeOffset now)
    {
        var remaining = activeEvent.End - now;
        Workspace.CurrentMission = new Mission
        {
            TaskId = ResolveCalendarTaskId(activeEvent),
            SourceType = "Calendar",
            SourceRecordId = activeEvent.Id,
            Title = CleanCalendarObjectiveTitle(activeEvent.Subject),
            Description = "Live Guardian calendar block",
            EstimatedDuration = remaining > TimeSpan.Zero
                ? remaining
                : TimeSpan.FromMinutes(1),
            StartBefore = activeEvent.Start.LocalDateTime,
            Status = "SCHEDULED NOW",
            ThreatLevel = "LOW",
            RecommendationReason =
                "This is the active item in Guardian's calendar plan."
        };

        SyncBriefingObjective(Workspace.CurrentMission);
        CalendarObjectiveAvailable = true;
        OnPropertyChanged(nameof(Workspace));
    }
    private static bool IsPlannedFocusBlock(CalendarEvent? calendarEvent) =>
        CalendarTimelineContextResolver.IsFocusBlock(calendarEvent);
    private static bool CalendarTitlesMatch(string calendarSubject, string candidateTitle)
    {
        static string Normalize(string value)
        {
            var cleaned = CleanCalendarObjectiveTitle(value)
                .Replace("Next action", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Task", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("Project", string.Empty, StringComparison.OrdinalIgnoreCase);
            return new string(cleaned.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        var calendar = Normalize(calendarSubject);
        var candidate = Normalize(candidateTitle);
        return calendar.Length >= 4 && candidate.Length >= 4 &&
               (calendar.Equals(candidate, StringComparison.Ordinal) ||
                calendar.Contains(candidate, StringComparison.Ordinal) ||
                candidate.Contains(calendar, StringComparison.Ordinal));
    }

    private static string CleanCalendarObjectiveTitle(string subject)
    {
        var separator = subject.IndexOf('·');
        return separator >= 0
            ? subject[(separator + 1)..].Trim()
            : subject.Replace("Focus", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(' ', '-', '–');
    }

    internal static long? ResolveCalendarTaskId(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null) return null;

        var marker = Regex.Match(
            calendarEvent.BodyPreview ?? string.Empty,
            @"NEKOMATA:(?:TASK:)?(?<id>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (marker.Success && long.TryParse(marker.Groups["id"].Value, out var markerId))
            return markerId;

        var title = Regex.Match(
            calendarEvent.Subject ?? string.Empty,
            @"(?:\(|\b)Task\s*#?\s*(?<id>\d+)\)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return title.Success && long.TryParse(title.Groups["id"].Value, out var titleId)
            ? titleId
            : null;
    }

    [RelayCommand]
    private async Task ShowCalendarAsync()
    {
        WorkspaceMode = WorkspaceMode.Calendar;
        if (!CalendarLoaded)
            await RefreshCalendarAsync();
    }

    [RelayCommand]
    private void ShowDashboard() => WorkspaceMode = WorkspaceMode.Dashboard;

    [RelayCommand]
    private async Task PreviousCalendarDayAsync()
    {
        SelectedCalendarDate = SelectedCalendarDate.AddDays(-1);
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task NextCalendarDayAsync()
    {
        SelectedCalendarDate = SelectedCalendarDate.AddDays(1);
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        SelectedCalendarDate = DateTime.Today;
        await RefreshCalendarAsync();
    }

    [RelayCommand]
    private async Task RefreshCalendarAsync()
    {
        if (CalendarBusy) return;
        CalendarBusy = true;
        CalendarStatus = "Syncing with Microsoft 365…";
        OnPropertyChanged(nameof(ShowCalendarEmptyState));

        try
        {
            var service = _services.GetRequiredService<ICalendarService>();
            var offset = TimeZoneInfo.Local.GetUtcOffset(SelectedCalendarDate);
            var start = new DateTimeOffset(SelectedCalendarDate.Date, offset);
            var events = await service.GetEventsAsync(start, start.AddDays(1));
            var anticipationEvents = events.ToList();
            if (SelectedCalendarDate.Date == DateTime.Today)
            {
                var tomorrow = await service.GetEventsAsync(start.AddDays(1), start.AddDays(2));
                anticipationEvents.AddRange(tomorrow.Where(x => anticipationEvents.All(y => y.Id != x.Id)));
            }
            var contextSignals=_services.GetRequiredService<IContextSignalRepository>();
            foreach(var calendarEvent in events)
                await contextSignals.UpsertAsync(ContextSignalFactory.FromCalendar(calendarEvent));
            await _services.GetRequiredService<TeamsContextEvidenceService>()
                .CorrelateAsync(Workspace,DateTimeOffset.Now);
            var ingestion = _services.GetRequiredService<GuardianOpenLoopIngestionService>();
            foreach (var calendarEvent in events)
                await ingestion.IngestMeetingScheduledAsync(calendarEvent.Id, calendarEvent.Subject);
            await _services.GetRequiredService<GuardianCalendarThreadEnrichmentService>()
                .EnrichAsync(Workspace, events, DateTimeOffset.Now);
            await _services.GetRequiredService<LearnedWorkflowEngine>()
                .ObserveAsync(Workspace, DateTimeOffset.Now);
            Workspace.WorkThreadSummaries = (await _services.GetRequiredService<GuardianWorkThreadEnrichmentService>()
                .BuildAsync(Workspace, DateTimeOffset.Now)).ToList();
            await _services.GetRequiredService<Nekomata.Core.Guardian.Anticipation.AnticipatoryOperationsEngine>()
                .EvaluateAsync(Workspace, anticipationEvents, DateTimeOffset.Now);
            CalendarEvents = new ObservableCollection<CalendarEvent>(events);
            ApplyCalendarCapacity(events);
            await RefreshCalendarAwareObjectiveAsync();
            CalendarLoaded = true;
            CalendarLastSyncedText = $"Calendar synced {DateTime.Now:HH:mm:ss}";
            CalendarStatus = events.Count == 0 ? "No meetings found for this day." : CalendarLastSyncedText;
        }
        catch (HttpRequestException ex)
        {
            CalendarStatus = $"Microsoft Graph could not be reached: {ex.Message}";
        }
        catch (Exception ex)
        {
            CalendarStatus = ex.Message;
        }
        finally
        {
            CalendarBusy = false;
            OnPropertyChanged(nameof(HasCalendarEvents));
            OnPropertyChanged(nameof(ShowCalendarEmptyState));
            OnPropertyChanged(nameof(CalendarDateLabel));
            OnPropertyChanged(nameof(CalendarSummary));
            RefreshAmbientSituation();
            CheckWorkloadForecastAttention();
        }
    }

    [RelayCommand]
    private void OpenCalendarThreadContext(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null) return;
        var context = Workspace.CalendarThreadContexts.FirstOrDefault(x => x.CalendarEventId == calendarEvent.Id);
        if (context?.ThreadId is not Guid threadId || context.LinkStatus != WorkThreadLinkStatus.Confirmed)
        {
            _threadContextResolverEvent = calendarEvent;
            ThreadContextResolverTitle = calendarEvent.Subject;
            ThreadContextResolverDetail = context is null || context.Confidence == 0
                ? "Guardian could not identify a confirmed Work Thread. Select the correct context."
                : $"Guardian found a {context.Confidence}% candidate ({context.Explanation}). Confirm it or select another thread.";
            ThreadContextResolverOptions = new ObservableCollection<GuardianWorkThreadSummary>(
                Workspace.WorkThreadSummaries
                    .OrderByDescending(x => context?.ThreadId == x.ThreadId)
                    .ThenByDescending(x => x.Impact.HasUpcomingCommitment)
                    .ThenBy(x => x.Title));
            ThreadContextDrawerVisible = false;
            ThreadContextResolverVisible = true;
            return;
        }
        ThreadContextDrawer = Workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == threadId);
        ThreadContextDrawerVisible = ThreadContextDrawer is not null;
        _threadContextDrawerEvent = calendarEvent;
        PopulateThreadTaskOptions(threadId);
    }

    [RelayCommand]
    private async Task ConfirmCalendarThreadContextAsync(GuardianWorkThreadSummary? selectedThread)
    {
        if (selectedThread is null || _threadContextResolverEvent is null) return;
        var calendarEvent = _threadContextResolverEvent;
        await _services.GetRequiredService<GuardianCalendarThreadEnrichmentService>()
            .ConfirmThreadAsync(calendarEvent, selectedThread.ThreadId);
        await _services.GetRequiredService<GuardianCalendarThreadEnrichmentService>()
            .EnrichAsync(Workspace, CalendarEvents.ToList(), DateTimeOffset.Now);
        Workspace.WorkThreadSummaries = (await _services.GetRequiredService<GuardianWorkThreadEnrichmentService>()
            .BuildAsync(Workspace, DateTimeOffset.Now)).ToList();
        ThreadContextResolverVisible = false;
        _threadContextResolverEvent = null;
        ThreadContextDrawer = Workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == selectedThread.ThreadId);
        ThreadContextDrawerVisible = ThreadContextDrawer is not null;
        _threadContextDrawerEvent = calendarEvent;
        PopulateThreadTaskOptions(selectedThread.ThreadId);
    }

    [RelayCommand]
    private async Task ConfirmCalendarTaskContextAsync(Nekomata.Models.Tasks.NekomataTask? task)
    {
        if (task is null || _threadContextDrawerEvent is null || ThreadContextDrawer is null) return;
        await _services.GetRequiredService<GuardianCalendarThreadEnrichmentService>()
            .ConfirmTaskAsync(_threadContextDrawerEvent, ThreadContextDrawer.ThreadId, task.Id);
        await _services.GetRequiredService<GuardianCalendarThreadEnrichmentService>()
            .EnrichAsync(Workspace, CalendarEvents.ToList(), DateTimeOffset.Now);
        Workspace.WorkThreadSummaries = (await _services.GetRequiredService<GuardianWorkThreadEnrichmentService>()
            .BuildAsync(Workspace, DateTimeOffset.Now)).ToList();
        ThreadContextDrawer = Workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == ThreadContextDrawer.ThreadId);
        ThreadContextTaskOptions.Clear();
        OnPropertyChanged(nameof(HasThreadContextTaskOptions));
    }

    private void PopulateThreadTaskOptions(Guid threadId)
    {
        var currentContext = _threadContextDrawerEvent is null ? null : Workspace.CalendarThreadContexts
            .FirstOrDefault(x => x.CalendarEventId == _threadContextDrawerEvent.Id);
        if (currentContext?.TaskId is not null)
        {
            ThreadContextTaskOptions.Clear();
            OnPropertyChanged(nameof(HasThreadContextTaskOptions));
            return;
        }
        var thread = Workspace.WorkThreads.FirstOrDefault(x => x.Id == threadId);
        ThreadContextTaskOptions = new ObservableCollection<Nekomata.Models.Tasks.NekomataTask>(
            thread?.PrimaryProjectId is long projectId
                ? Workspace.Tasks.Where(x => x.ProjectId == projectId && !x.IsCompleted).OrderBy(x => x.Title)
                : []);
        OnPropertyChanged(nameof(HasThreadContextTaskOptions));
    }

    [RelayCommand]
    private void CloseThreadContextResolver()
    {
        ThreadContextResolverVisible = false;
        _threadContextResolverEvent = null;
    }

    [RelayCommand]
    private void CloseThreadContextDrawer()
    {
        ThreadContextDrawerVisible = false;
        ThreadContextDrawer = null;
        ThreadContextTaskOptions.Clear();
        OnPropertyChanged(nameof(HasThreadContextTaskOptions));
        _threadContextDrawerEvent = null;
    }


    private void ApplyGuardianCalendarGuidance()
    {
        if (_nowTimelineEvent is not null)
        {
            Workspace.Briefing.GuardianComment =
                $"Calendar priority: continue with '{CleanCalendarObjectiveTitle(_nowTimelineEvent.Subject)}' until {_nowTimelineEvent.End:HH:mm}.";
            return;
        }

        if (_nextTimelineEvent is not null)
        {
            Workspace.Briefing.GuardianComment =
                $"Calendar priority: use this free window until {_nextTimelineEvent.Start:HH:mm}, then move to '{CleanCalendarObjectiveTitle(_nextTimelineEvent.Subject)}'.";
            return;
        }

        Workspace.Briefing.GuardianComment =
            "Your calendar is clear for the rest of the workday; use this window for the highest-ranked available mission.";
    }    private void UpdateLiveTimeline(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        _calendarContext = CalendarTimelineContextResolver.Resolve(events, now);
        NowTimelineText = _nowTimelineEvent is null ? "NOW · Free time" : $"NOW · {CleanCalendarObjectiveTitle(_nowTimelineEvent.Subject)} · until {_nowTimelineEvent.End:HH:mm}";
        NextTimelineText = _nextTimelineEvent is null ? "NEXT · Nothing else scheduled" : $"NEXT · {_nextTimelineEvent.Start:HH:mm} · {CleanCalendarObjectiveTitle(_nextTimelineEvent.Subject)}";
        ThenTimelineText = _thenTimelineEvent is null ? "THEN · —" : $"THEN · {_thenTimelineEvent.Start:HH:mm} · {CleanCalendarObjectiveTitle(_thenTimelineEvent.Subject)}";
        ApplyGuardianCalendarGuidance();
        OnPropertyChanged(nameof(NowTimelineActionText));
        OnPropertyChanged(nameof(HasNowTimelineAction));
        OnPropertyChanged(nameof(HasNextTimelineAction));
        OnPropertyChanged(nameof(HasThenTimelineAction));
        EvaluateCalendarPlanHealth(events, now);
        EvaluateTransitionProtection(events, now);
    }

    private void InitialisePlanHealthMonitoring()
    {
        _planHealthTimer.Interval = TimeSpan.FromSeconds(30);
        _planHealthTimer.Tick += async (_, _) =>
        {
            await RefreshDailyBriefingContextAsync();
            await RefreshCalendarAwareObjectiveAsync();
        };
        _planHealthTimer.Start();
    }

    private void EvaluateCalendarPlanHealth(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        var active = events.FirstOrDefault(item => item.Start <= now && item.End > now);
        var managed = events.Where(item => item.IsNekomataManaged).ToList();
        var protectedEvents = events.Where(item => !item.IsNekomataManaged).ToList();
        var conflict = managed
            .Where(block => block.End > now)
            .Select(block => new
            {
                Block = block,
                Conflict = protectedEvents.FirstOrDefault(item =>
                    item.IsAllDay || (item.Start < block.End && item.End > block.Start))
            })
            .FirstOrDefault(item => item.Conflict is not null);

        if (_unworkedBlockKey is not null && _unworkedBlockEnd <= now &&
            !string.Equals(_dismissedPlanAlertKey, $"missed:{_unworkedBlockKey}", StringComparison.Ordinal))
        {
            SetPlanAlert(
                $"missed:{_unworkedBlockKey}",
                "PLANNED BLOCK MISSED",
                "A planned focus block ended without an active matching mission. Guardian can repair the remainder of today.",
                null);
            _unworkedBlockKey = null;
            _unworkedBlockEnd = null;
            return;
        }

        if (IsPlannedFocusBlock(active) && !IsWorkingOnCalendarEvent(active!))
        {
            var focusEvent = active!;
            _unworkedBlockKey = focusEvent.Id;
            _unworkedBlockEnd = focusEvent.End;
            SetPlanAlert(
                $"inactive:{focusEvent.Id}",
                "PLANNED BLOCK NOT ACTIVE",
                $"'{CleanCalendarObjectiveTitle(focusEvent.Subject)}' was scheduled from {focusEvent.Start:HH:mm} to {focusEvent.End:HH:mm}, but no matching mission is running.",
                focusEvent);
            return;
        }

        if (conflict is not null)
        {
            SetPlanAlert(
                $"conflict:{conflict.Block.Id}:{conflict.Conflict!.Id}",
                "CALENDAR PLAN NEEDS REPAIR",
                $"'{CleanCalendarObjectiveTitle(conflict.Block.Subject)}' overlaps '{CleanCalendarObjectiveTitle(conflict.Conflict.Subject)}'.",
                null);
            return;
        }

        ResolveAttentionByPrefix("calendar:plan:");

        PlanAttentionVisible = false;
        CanStartPlanAlert = false;
        _planAlertEvent = null;
        PlanHealthTitle = CalendarTimelineContextResolver.IsFocusBlock(active)
            ? "PLAN HEALTH · Focus active"
            : active is not null
                ? "PLAN HEALTH · Protected meeting"
                : "PLAN HEALTH · On track";
        PlanHealthDetail = active is null
            ? "No calendar conflict is currently affecting the plan."
            : $"Current block ends at {active.End:HH:mm}. Guardian is watching the next transition.";
    }

    private bool IsWorkingOnCalendarEvent(CalendarEvent calendarEvent)
    {
        if (!MissionActive) return false;
        var mission = Workspace.CurrentMission;
        return calendarEvent.Subject.Contains(mission.Title, StringComparison.OrdinalIgnoreCase) ||
            (mission.TaskId is long taskId &&
             (calendarEvent.BodyPreview.Contains($"NEKOMATA:TASK:{taskId}", StringComparison.OrdinalIgnoreCase) ||
              calendarEvent.BodyPreview.Contains($"NEKOMATA:{taskId}", StringComparison.OrdinalIgnoreCase)));
    }

    private void SetPlanAlert(string key, string title, string detail, CalendarEvent? startEvent)
    {
        if (string.Equals(_dismissedPlanAlertKey, key, StringComparison.Ordinal)) return;
        PlanHealthTitle = title;
        PlanHealthDetail = detail;
        _planAlertEvent = startEvent;
        CanStartPlanAlert = startEvent is not null;
        PlanAttentionVisible = true;
        var attentionKey = $"calendar:plan:{key}";
        ResolveAttentionByPrefix("calendar:plan:", attentionKey);
        RaiseAttention(
            attentionKey,
            "CALENDAR PLAN",
            title.Contains("MISSED", StringComparison.OrdinalIgnoreCase) ? "High" : "Warning",
            title,
            detail,
            startEvent is null ? "open_calendar" : "start_calendar",
            startEvent?.Id,
            expiresAt: (startEvent?.End ?? DateTimeOffset.Now.AddHours(4)).AddMinutes(5));
        if (!string.Equals(_lastAlertSoundKey, key, StringComparison.Ordinal))
        {
            SystemSounds.Exclamation.Play();
            _lastAlertSoundKey = key;
        }
    }

    [RelayCommand]
    private async Task StartPlanAlertAsync()
    {
        if (_planAlertEvent is null) return;
        SetActiveFocusObjective(_planAlertEvent, DateTimeOffset.Now);
        await BeginMissionAsync();
        ResolveAttentionByPrefix("calendar:plan:");
        PlanAttentionVisible = false;
    }

    [RelayCommand]
    private async Task RepairCalendarPlanAsync()
    {
        ChatInput = $"Repair the remainder of today's calendar plan from {DateTime.Now:HH:mm}. {PlanHealthDetail} Keep protected meetings fixed, preserve completed work, and reschedule unfinished ranked tasks into valid free windows.";
        GuardianPanelExpanded = true;
        ResolveAttentionByPrefix("calendar:plan:");
        PlanAttentionVisible = false;
        await SendGuardianMessageAsync();
    }

    [RelayCommand]
    private void DismissPlanAlert()
    {
        _dismissedPlanAlertKey = _planAlertEvent is not null
            ? $"inactive:{_planAlertEvent.Id}"
            : _lastAlertSoundKey;
        ResolveAttentionByPrefix("calendar:plan:");
        PlanAttentionVisible = false;
    }

    [RelayCommand]
    private async Task ActivateNowTimelineAsync()
    {
        if (_calendarContext.HasActiveFocus && CalendarObjectiveAvailable)
        {
            await BeginMissionAsync();
            return;
        }
        OpenCalendarEvent(_nowTimelineEvent);
    }

    [RelayCommand]
    private void OpenNextTimeline() => OpenCalendarEvent(_nextTimelineEvent);

    [RelayCommand]
    private void OpenThenTimeline() => OpenCalendarEvent(_thenTimelineEvent);

    [RelayCommand]
    private void AdjustNextTimeline()
    {
        if (_nextTimelineEvent is null) return;
        ChatInput = $"Please adjust my next calendar block '{CleanCalendarObjectiveTitle(_nextTimelineEvent.Subject)}' at {_nextTimelineEvent.Start:HH:mm}–{_nextTimelineEvent.End:HH:mm}. ";
        GuardianPanelExpanded = true;
    }

    private static void OpenCalendarEvent(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null || string.IsNullOrWhiteSpace(calendarEvent.WebLink)) return;
        Process.Start(new ProcessStartInfo(calendarEvent.WebLink) { UseShellExecute = true });
    }

    private void ScheduleNextCalendarBoundary(IReadOnlyList<CalendarEvent> events, DateTimeOffset now)
    {
        var boundary = events.SelectMany(item => new[] { item.Start, item.End }).Where(value => value > now).OrderBy(value => value).FirstOrDefault();
        _calendarBoundaryTimer.Stop();
        if (boundary == default) return;
        _calendarBoundaryTimer.Interval = boundary - now + TimeSpan.FromSeconds(1);
        _calendarBoundaryTimer.Tick -= CalendarBoundaryTimer_Tick;
        _calendarBoundaryTimer.Tick += CalendarBoundaryTimer_Tick;
        _calendarBoundaryTimer.Start();
    }

    private async void CalendarBoundaryTimer_Tick(object? sender, EventArgs e)
    {
        _calendarBoundaryTimer.Stop();
        await RefreshCalendarAwareObjectiveAsync();
    }

    private static string FormatDuration(int minutes) => minutes < 60 ? $"{minutes}m" : $"{minutes / 60}h {minutes % 60:00}m";

    private void ApplyCalendarCapacity(IReadOnlyList<CalendarEvent> events)
    {
        if (SelectedCalendarDate.Date != DateTime.Today) return;
        ApplyTodayCalendarCapacity(events);
    }

    private void ApplyTodayCalendarCapacity(IReadOnlyList<CalendarEvent> events)
    {
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var today = DateTime.Today;
        var offset = TimeZoneInfo.Local.GetUtcOffset(today);
        var workStart = new DateTimeOffset(settings.GetStart(today), offset);
        var workEnd = new DateTimeOffset(settings.GetEnd(today), offset);

        var lunchStart = settings.IncludeLunchBreak
            ? new DateTimeOffset(settings.GetLunchStart(today), offset)
            : (DateTimeOffset?)null;
        var lunchEnd = settings.IncludeLunchBreak
            ? new DateTimeOffset(settings.GetLunchEnd(today), offset)
            : (DateTimeOffset?)null;
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> BuildIntervals(
            IEnumerable<CalendarEvent> source) => CalendarCapacityIntervalCalculator.Calculate(
                source.Select(item => item.IsAllDay
                    ? (Start: workStart, End: workEnd)
                    : (item.Start, item.End)),
                workStart,
                workEnd,
                lunchStart,
                lunchEnd);

        _calendarCapacitySnapshotDate = today;
        _hasCalendarCapacitySnapshot = true;
        _cachedCalendarCapacityIntervals = BuildIntervals(events);
        _cachedScheduledFocusMinutesToday = BuildIntervals(events.Where(item => item.IsNekomataManaged))
            .Sum(interval => Math.Max(0, (int)(interval.End - interval.Start).TotalMinutes));

        ApplyCachedTodayCalendarCapacity(DateTimeOffset.Now);
        RefreshTimeAwareCapacity();
    }

    private void ApplyCachedTodayCalendarCapacity(DateTimeOffset now)
    {
        if (!_hasCalendarCapacitySnapshot || _calendarCapacitySnapshotDate != now.LocalDateTime.Date)
            return;

        var capacity = Workspace.Capacity;
        capacity.ScheduledMinutesToday = Math.Min(
            _cachedCalendarCapacityIntervals.Sum(interval =>
                Math.Max(0, (int)(interval.End - interval.Start).TotalMinutes)),
            capacity.WorkingMinutesToday);
        capacity.ScheduledMinutesRemaining = Math.Min(
            _cachedCalendarCapacityIntervals
                .Where(interval => interval.End > now)
                .Sum(interval => Math.Max(0,
                    (int)(interval.End - (interval.Start > now ? interval.Start : now)).TotalMinutes)),
            capacity.WorkingMinutesToday);
        capacity.ScheduledFocusMinutesToday = Math.Min(
            _cachedScheduledFocusMinutesToday,
            capacity.ScheduledMinutesToday);
        Workspace.Briefing.PlannedMinutesToday =
            capacity.ScheduledFocusMinutesToday;
    }
    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> ExcludeLunch(
        (DateTimeOffset Start, DateTimeOffset End) interval,
        WorkingDaySettings settings,
        DateTime date,
        TimeSpan offset)
    {
        if (!settings.IncludeLunchBreak)
        {
            yield return interval;
            yield break;
        }

        var lunchStart = new DateTimeOffset(settings.GetLunchStart(date), offset);
        var lunchEnd = new DateTimeOffset(settings.GetLunchEnd(date), offset);
        if (interval.End <= lunchStart || interval.Start >= lunchEnd)
        {
            yield return interval;
            yield break;
        }

        if (interval.Start < lunchStart)
            yield return (interval.Start, lunchStart);
        if (interval.End > lunchEnd)
            yield return (lunchEnd, interval.End);
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> MergeCalendarIntervals(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> intervals)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var interval in intervals)
        {
            if (interval.End <= interval.Start) continue;
            if (merged.Count == 0 || interval.Start > merged[^1].End)
            {
                merged.Add(interval);
                continue;
            }

            var previous = merged[^1];
            merged[^1] = (previous.Start, interval.End > previous.End ? interval.End : previous.End);
        }
        return merged;
    }

    [RelayCommand]
    private async Task UndoLastCalendarPlanAsync()
    {
        if (!CanUndoCalendarPlan)
        {
            CalendarSchedulingStatus = "There is no Guardian calendar plan to undo.";
            return;
        }

        var undo = _services.GetRequiredService<CalendarUndoService>();
        var targetDate = undo.LastPlanDate;
        CalendarSchedulingStatus = await undo.UndoLastAsync();
        if (targetDate.HasValue)
            SelectedCalendarDate = targetDate.Value;
        await RefreshCalendarAsync();
        OnPropertyChanged(nameof(CanUndoCalendarPlan));
    }

    [RelayCommand]
    private async Task ScheduleDayAsync()
    {
        if (CalendarScheduling) return;
        CalendarScheduling = true;
        try
        {
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var targetDate = SelectedCalendarDate.Date > DateTime.Today
                ? SelectedCalendarDate.Date
                : DateTime.Now >= settings.GetEnd(DateTime.Today) || Workspace.Capacity.IsWorkingOvertime
                    ? DateTime.Today.AddDays(1)
                    : DateTime.Today;
            while (targetDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                targetDate = targetDate.AddDays(1);

            var offset = TimeZoneInfo.Local.GetUtcOffset(targetDate);
            var dayStart = new DateTimeOffset(settings.GetStart(targetDate), offset);
            var dayEnd = new DateTimeOffset(settings.GetEnd(targetDate), offset);
            var service = _services.GetRequiredService<ICalendarService>();
            var undo = _services.GetRequiredService<CalendarUndoService>();
            undo.BeginBatch();
            var existing = (await service.GetEventsAsync(dayStart, dayEnd)).ToList();
            var busy = existing.Where(item => !item.IsAllDay)
                .Select(item => (Start: item.Start, End: item.End)).ToList();
            if (settings.IncludeLunchBreak)
                busy.Add((new DateTimeOffset(settings.GetLunchStart(targetDate), offset), new DateTimeOffset(settings.GetLunchEnd(targetDate), offset)));

            var candidates = Workspace.RankedMissionCandidates
                .Where(candidate => candidate.TaskId.HasValue)
                .Where(candidate => candidate.EstimatedMinutes > 0)
                .Where(candidate => candidate.DueAt is null || candidate.DueAt.Value.Date <= targetDate.Date.AddDays(1))
                .Where(candidate => !existing.Any(item =>
                    item.Subject.Equals($"Focus · {candidate.Title}", StringComparison.OrdinalIgnoreCase) ||
                    item.BodyPreview.Contains($"NEKOMATA:{candidate.TaskId}", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(candidate => candidate.RequiresImmediateAttention)
                .ThenBy(candidate => candidate.Rank)
                .ThenByDescending(candidate => candidate.Score)
                .Take(10)
                .ToList();

            var created = 0;
            foreach (var candidate in candidates)
            {
                var duration = TimeSpan.FromMinutes(Math.Clamp(candidate.EstimatedMinutes, settings.MinimumFocusBlockMinutes, 120));
                var slot = FindFreeSlot(dayStart, dayEnd, duration, busy);
                if (slot is null) continue;
                var marker = $"NEKOMATA:{candidate.TaskId}";
                var calendarEvent = await service.CreateFocusEventAsync(candidate.Title, slot.Value.Start, slot.Value.End, marker);
                undo.RecordCreated(calendarEvent.Id, candidate.Title, slot.Value.Start);
                existing.Add(calendarEvent);
                busy.Add(slot.Value);
                created++;
            }

            undo.CommitBatch();
            OnPropertyChanged(nameof(CanUndoCalendarPlan));

            SelectedCalendarDate = targetDate;
            CalendarSchedulingStatus = created == 0
                ? $"No new focus blocks were needed or available for {targetDate:dddd}."
                : $"Scheduled {created} ranked focus block{(created == 1 ? string.Empty : "s")} for {targetDate:dddd}.";
            await RefreshCalendarAsync();
        }
        catch (Exception ex)
        {
            CalendarSchedulingStatus = $"Scheduling failed: {ex.Message}";
        }
        finally
        {
            CalendarScheduling = false;
        }
    }

    private static (DateTimeOffset Start, DateTimeOffset End)? FindFreeSlot(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        TimeSpan duration,
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied)
    {
        var cursor = dayStart;
        foreach (var block in occupied.OrderBy(block => block.Start))
        {
            if (block.End <= cursor || block.Start >= dayEnd) continue;
            if (block.Start - cursor >= duration) return (cursor, cursor + duration);
            if (block.End > cursor) cursor = block.End;
        }
        return dayEnd - cursor >= duration ? (cursor, cursor + duration) : null;
    }
}
