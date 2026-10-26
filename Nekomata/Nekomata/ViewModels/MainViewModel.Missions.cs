using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Models.Guardian;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private bool missionActive;

    [ObservableProperty]
    private bool missionPaused;

    [ObservableProperty]
    private bool missionOverrun;

    [ObservableProperty]
    private string missionElapsed = "00:00:00";

    [ObservableProperty]
    private string missionRemaining = "00:00:00";

    [ObservableProperty]
    private DateTime? missionStartedAt;

    [ObservableProperty]
    private string missionPauseButtonText = "PAUSE";

    [ObservableProperty]
    private string missionStateText = "FOCUS ACTIVE";

    [ObservableProperty]
    private double missionSessionProgress;

    [ObservableProperty]
    private TimeSpan missionSessionEstimate;

    [ObservableProperty] private bool missionCheckInVisible;
    [ObservableProperty] private string missionCheckInTitle = "GUARDIAN CHECK-IN";
    [ObservableProperty] private string missionCheckInDetail = "Are you still making progress?";
    [ObservableProperty] private string missionCheckInStatus = "Guardian will check in during this focus block.";
    [ObservableProperty] private string missionBlockerNote = "";
    private TimeSpan _nextMissionCheckInAt;
    private bool _missionOverrunCheckInShown;
    private Nekomata.Models.Missions.Mission? _activeMission;
    private readonly List<MissionWorkSegment> _missionWorkSegments = [];

    public string ObjectiveActionText => MissionEntryPolicy.Resolve(MissionActive, MissionPaused) switch
    {
        MissionEntryAction.ResumePausedMission => "RESUME CURRENT OBJECTIVE",
        MissionEntryAction.ReturnToActiveMission => "RETURN TO ACTIVE OBJECTIVE",
        _ => "BEGIN TODAY'S OBJECTIVE"
    };

    partial void OnMissionPausedChanged(bool value) => OnPropertyChanged(nameof(ObjectiveActionText));

    [RelayCommand]
    private async Task BeginMissionAsync()
    {
        var entryAction = MissionEntryPolicy.Resolve(MissionActive, MissionPaused);
        if (entryAction is not MissionEntryAction.Start)
        {
            ReturnToActiveMission();
            if (entryAction is MissionEntryAction.ResumePausedMission)
                ToggleMissionPause();
            return;
        }

        var mission = Workspace.CurrentMission;

        if (mission is null)
            return;

        _activeMission = mission;
        _manualObjectiveOverride = null;
        MissionActive = true;
        MissionPaused = false;
        MissionOverrun = false;

        MissionPauseButtonText = "PAUSE";
        MissionStateText = "FOCUS ACTIVE";
        MissionSessionProgress = 0;
        MissionSessionEstimate = GetMissionSessionEstimate(mission, DateTimeOffset.Now);
        MissionCheckInVisible = false;
        MissionBlockerNote = string.Empty;
        _missionOverrunCheckInShown = false;
        var firstCheckMinutes = Math.Clamp(MissionSessionEstimate.TotalMinutes / 3d, 10d, 25d);
        _nextMissionCheckInAt = TimeSpan.FromMinutes(firstCheckMinutes);
        MissionCheckInStatus = $"Next Guardian check-in at approximately {DateTime.Now.Add(_nextMissionCheckInAt):HH:mm}.";

        MissionStartedAt = DateTime.Now;
        _missionSegmentStartedAt = DateTime.Now;
        _missionAccumulatedElapsed = TimeSpan.Zero;
        _missionWorkSegments.Clear();

        WorkspaceMode = Models.Workspace.WorkspaceMode.Mission;

        _missionTimer.Start();

        UpdateMissionTimer();
        await BeginPolicyObservationAsync(mission);

        try
        {
            var memoryRepository =
                _services.GetRequiredService<
                    IGuardianMemoryRepository>();

            await memoryRepository.AddAsync(
                new GuardianMemory
                {
                    Category = "MissionStarted",
                    Importance = 55,
                    Source = "User",

                    Summary =
                        $"Started mission '{mission.Title}'.",

                    Detail = $"""
                        Score: {mission.Score}
                        Threat level: {mission.ThreatLevel}
                        Estimated duration: {mission.EstimatedDuration}
                        Business value: {mission.BusinessValue:C}
                        """,

                    TaskId = mission.TaskId,
                    ProjectId = mission.ProjectId
                });
        }
        catch (Exception ex)
        {
            GuardianResponse =
                "Mission started, but memory could not be " +
                $"stored: {ex.Message}";
        }
    }

    private void ReturnToActiveMission()
    {
        if (!MissionActive || _activeMission is null)
            return;

        Workspace.CurrentMission = _activeMission;
        WorkspaceMode = Models.Workspace.WorkspaceMode.Mission;
        UpdateMissionTimer();
        OnPropertyChanged(nameof(Workspace));
    }

    private void UpdateMissionTimer()
    {
        if (!MissionActive)
            return;

        var mission = Workspace.CurrentMission;

        if (mission is null)
            return;

        var currentSegment =
            !MissionPaused &&
            _missionSegmentStartedAt is not null
                ? DateTime.Now -
                  _missionSegmentStartedAt.Value
                : TimeSpan.Zero;

        var elapsed =
            _missionAccumulatedElapsed +
            currentSegment;

        var estimated =
            MissionSessionEstimate;

        MissionElapsed =
            elapsed.ToString(@"hh\:mm\:ss");

        var remaining =
            estimated - elapsed;

        MissionOverrun =
            remaining < TimeSpan.Zero;

        if (MissionOverrun)
        {
            var overrun =
                elapsed - estimated;

            MissionRemaining = "+" + MissionClock(overrun);

            MissionStateText =
                MissionPaused
                    ? "MISSION PAUSED"
                    : "MISSION OVERRUN";
        }
        else
        {
            // Minutes and seconds under an hour, so the countdown fits inside the core.
            MissionRemaining = MissionClock(remaining);

            MissionStateText =
                MissionPaused
                    ? "MISSION PAUSED"
                    : "FOCUS ACTIVE";
        }

        var estimatedSeconds =
            Math.Max(
                estimated.TotalSeconds,
                1);

        MissionSessionProgress =
            Math.Clamp(
                elapsed.TotalSeconds /
                estimatedSeconds * 100,
                0,
                100);
        EvaluateMissionCheckIn(elapsed);
    }

    private void EvaluateMissionCheckIn(TimeSpan elapsed)
    {
        if (!MissionActive || MissionPaused) return;
        if (MissionOverrun && !_missionOverrunCheckInShown)
        {
            _missionOverrunCheckInShown = true;
            MissionCheckInTitle = "MISSION OVERRUN";
            MissionCheckInDetail = "This focus block has exceeded its estimate. Finish, add time, or tell Guardian what is blocking progress.";
            MissionCheckInVisible = true;
            System.Media.SystemSounds.Exclamation.Play();
            return;
        }
        if (!MissionCheckInVisible && elapsed >= _nextMissionCheckInAt)
        {
            MissionCheckInTitle = "GUARDIAN CHECK-IN";
            MissionCheckInDetail = $"You have been working on '{Workspace.CurrentMission.Title}' for {(int)elapsed.TotalMinutes} minutes. How is it going?";
            MissionCheckInVisible = true;
            System.Media.SystemSounds.Asterisk.Play();
        }
    }

    [RelayCommand]
    private async Task MissionOnTrackAsync()
    {
        var elapsed = GetCurrentMissionElapsed();
        MissionCheckInVisible = false;
        _nextMissionCheckInAt = elapsed.Add(TimeSpan.FromMinutes(20));
        MissionCheckInStatus = $"On track · next check-in around {DateTime.Now.AddMinutes(20):HH:mm}.";
        await RecordMissionCheckInAsync("OnTrack", $"Confirmed on track after {(int)elapsed.TotalMinutes} minutes.");
    }

    [RelayCommand]
    private async Task MissionBlockedAsync()
    {
        var reason = string.IsNullOrWhiteSpace(MissionBlockerNote) ? "No blocker detail supplied." : MissionBlockerNote.Trim();
        if (!MissionPaused) ToggleMissionPause();
        MissionCheckInVisible = false;
        MissionCheckInStatus = $"Blocked · {reason}";
        await RecordMissionCheckInAsync("Blocked", reason);
        ChatInput = $"I am blocked on '{Workspace.CurrentMission.Title}'. Blocker: {reason} Replan the remainder of today around the live calendar, preserving protected meetings and important commitments.";
        GuardianPanelExpanded = true;
        await SendGuardianMessageAsync();
    }

    [RelayCommand]
    private async Task MissionFinishedFromCheckInAsync()
    {
        MissionCheckInVisible = false;
        await RecordMissionCheckInAsync("Finished", "Marked finished from Guardian check-in.");
        await CompleteMissionAsync();
    }

    [RelayCommand]
    private async Task MissionNeedsMoreTimeAsync()
    {
        var reason = string.IsNullOrWhiteSpace(MissionBlockerNote) ? "Work requires more time than estimated." : MissionBlockerNote.Trim();
        Workspace.CurrentMission.EstimatedDuration += TimeSpan.FromMinutes(30);
        MissionSessionEstimate += TimeSpan.FromMinutes(30);
        _missionOverrunCheckInShown = false;
        MissionOverrun = false;
        MissionCheckInVisible = false;
        _nextMissionCheckInAt = GetCurrentMissionElapsed().Add(TimeSpan.FromMinutes(15));
        MissionCheckInStatus = $"Estimate extended by 30 minutes · next check-in around {DateTime.Now.AddMinutes(15):HH:mm}.";
        OnPropertyChanged(nameof(Workspace));
        await RecordMissionCheckInAsync("Extended", $"Added 30 minutes. Reason: {reason}");
    }

    private TimeSpan GetMissionSessionEstimate(
        Nekomata.Models.Missions.Mission mission,
        DateTimeOffset now) =>
        MissionSessionEstimateCalculator.Calculate(
            mission.EstimatedDuration,
            mission.Status.Equals(
                "SCHEDULED NOW",
                StringComparison.OrdinalIgnoreCase),
            now,
            _calendarContext.Active?.End);
    private TimeSpan GetCurrentMissionElapsed()
    {
        var currentSegment = !MissionPaused && _missionSegmentStartedAt is not null
            ? DateTime.Now - _missionSegmentStartedAt.Value
            : TimeSpan.Zero;
        return _missionAccumulatedElapsed + currentSegment;
    }

    private async Task RecordMissionCheckInAsync(string outcome, string detail)
    {
        try
        {
            var mission = Workspace.CurrentMission;
            var memoryRepository = _services.GetRequiredService<IGuardianMemoryRepository>();
            await memoryRepository.AddAsync(new GuardianMemory
            {
                Category = $"MissionCheckIn{outcome}",
                Importance = outcome is "Blocked" or "Extended" ? 80 : 50,
                Source = "User",
                Summary = $"{outcome}: {mission.Title}",
                Detail = detail,
                TaskId = mission.TaskId,
                ProjectId = mission.ProjectId
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Mission check-in memory failed: {ex}");
        }
    }

    [RelayCommand]
    private void ToggleMissionPause()
    {
        if (!MissionActive)
            return;

        if (MissionPaused)
        {
            MissionPaused = false;
            MissionPauseButtonText = "PAUSE";

            MissionStateText =
                MissionOverrun
                    ? "MISSION OVERRUN"
                    : "FOCUS ACTIVE";

            _missionSegmentStartedAt =
                DateTime.Now;

            _missionTimer.Start();
        }
        else
        {
            if (_missionSegmentStartedAt is not null)
            {
                _missionWorkSegments.Add(new MissionWorkSegment(_missionSegmentStartedAt.Value, DateTime.Now));
                _missionAccumulatedElapsed +=
                    DateTime.Now -
                    _missionSegmentStartedAt.Value;
            }

            _missionSegmentStartedAt = null;

            MissionPaused = true;
            MissionPauseButtonText = "RESUME";
            MissionStateText = "MISSION PAUSED";

            _missionTimer.Stop();

            UpdateMissionTimer();
        }
    }

    [RelayCommand]
    private async Task HoldMissionForLaterAsync()
    {
        if (!MissionActive)
            return;

        var mission = Workspace.CurrentMission;
        var elapsed = GetCurrentMissionElapsed();
        _missionTimer.Stop();

        try
        {
            MissionEffortUpdate? effort = null;
            if (mission.TaskId is long taskId)
            {
                var taskRepository = _services.GetRequiredService<ITaskRepository>();
                var task = await taskRepository.GetByIdAsync(taskId);
                if (task is not null)
                {
                    effort = MissionEffortTracker.ApplyElapsed(task, elapsed);
                    await taskRepository.SaveAsync(task);
                    mission.Progress = effort.Progress;
                }
            }

            await _missionSessionService.RecordDeferredMissionAsync(
                mission,
                MissionStartedAt ?? DateTime.Now,
                elapsed,
                CaptureMissionWorkSegments(MissionStartedAt ?? DateTime.Now, elapsed));
            await BackfillWorkedObjectiveToCalendarAsync(mission, MissionStartedAt ?? DateTime.Now, elapsed);
            await CompletePolicyObservationAsync(mission, "Adverse", 0);
            await RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "ObjectiveDeferred", Category = "Mission", Title = "Objective deferred",
                Narrative = $"{mission.Title} deferred after {(int)Math.Ceiling(elapsed.TotalMinutes)} minutes; {Math.Max(0, (int)Math.Ceiling((MissionSessionEstimate - elapsed).TotalMinutes))} predicted minutes remain.",
                TaskId = mission.TaskId, ProjectId = mission.ProjectId, BusinessValue = mission.BusinessValue,
                Outcome = "Deferred", Significance = 70, RecommendationId = _activeRecommendationId,
                CorrelationId = _activeCorrelationId ?? Guid.NewGuid(), ParentEventId = _activeDecisionEventId,
                CausedByEventId = _activeDecisionEventId,
                Recommendation = _activeRecommendationId is null ? "" : $"Start {mission.Title}",
                UserDecision = _activeRecommendationId is null ? "" : "Accepted"
            });
            _activeRecommendationId = null;
            _activeCorrelationId = null;
            _activeDecisionEventId = null;

            var workedMinutes = effort?.WorkedMinutes ??
                Math.Max(0, (int)Math.Ceiling(elapsed.TotalMinutes));
            var remainingText = effort is null
                ? "The task remains available for another session."
                : $"{effort.RemainingMinutes} estimated minutes remain.";

            GuardianResponse =
                $"Mission held for later after {workedMinutes} focused minute{(workedMinutes == 1 ? string.Empty : "s")}. {remainingText}";
            ChatHistory.Add(new()
            {
                Role = "assistant",
                Content = GuardianResponse
            });
            GuardianPanelExpanded = true;

            ExitMission();
            await _workspaceCoordinator.RefreshAsync();
            await RefreshAnalyticsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Hold Mission Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _missionTimer.Start();
        }
    }
    [RelayCommand]
    private void ExitMission()
    {
        _missionTimer.Stop();
        // Allow a fresh suggestion after the session transition and source refresh settle.
        if (_nextActionState != null)
        {
            _nextActionState.LastPrompt = DateTimeOffset.Now.AddMinutes(-43);
            try { SaveNextActionState(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Could not save next-action cooldown: " + ex.Message); }
        }

        MissionActive = false;
        _activeMission = null;
        _manualObjectiveOverride = null;
        MissionPaused = false;
        MissionOverrun = false;
        MissionCheckInVisible = false;
        MissionBlockerNote = string.Empty;

        MissionPauseButtonText = "PAUSE";
        MissionStateText = "FOCUS ACTIVE";
        MissionSessionProgress = 0;
        MissionSessionEstimate = TimeSpan.Zero;

        MissionStartedAt = null;
        _missionSegmentStartedAt = null;
        _missionAccumulatedElapsed = TimeSpan.Zero;
        _missionWorkSegments.Clear();

        MissionElapsed = "00:00:00";
        MissionRemaining = "00:00:00";
        CalendarObjectiveAvailable = false;
        CanBeginBriefingObjective = false;

        WorkspaceMode =
            Models.Workspace.WorkspaceMode.Dashboard;
    }

    [RelayCommand]
    private async Task CompleteMissionAsync()
    {
        if (!MissionActive)
            return;

        var mission = Workspace.CurrentMission;

        if (mission is null)
            return;

        _missionTimer.Stop();

        var currentSegment =
            !MissionPaused &&
            _missionSegmentStartedAt is not null
                ? DateTime.Now -
                  _missionSegmentStartedAt.Value
                : TimeSpan.Zero;

        var elapsed =
            _missionAccumulatedElapsed +
            currentSegment;

        try
        {
            var memoryRepository =
                _services.GetRequiredService<
                    IGuardianMemoryRepository>();

            if (mission.TaskId is null &&
                mission.SourceType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
            {
                mission.TaskId = ResolveCalendarTaskId(FindCalendarEventForMission(mission, DateTimeOffset.Now));
            }

            if (mission.TaskId is not null)
            {
                var taskRepository =
                    _services.GetRequiredService<
                        ITaskRepository>();

                await taskRepository.CompleteAsync(
                    mission.TaskId.Value);
            }

            await _missionSessionService
                .RecordCompletedMissionAsync(
                    mission,
                    MissionStartedAt ?? DateTime.Now,
                    elapsed,
                    CaptureMissionWorkSegments(MissionStartedAt ?? DateTime.Now, elapsed));
            await BackfillWorkedObjectiveToCalendarAsync(mission, MissionStartedAt ?? DateTime.Now, elapsed);
            await CompletePolicyObservationAsync(mission, "Successful", Math.Max(0, (int)Math.Ceiling(elapsed.TotalMinutes)));
            await RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "ObjectiveCompleted", Category = "Mission", Title = "Objective completed",
                Narrative = $"{mission.Title} completed in {(int)Math.Ceiling(elapsed.TotalMinutes)}m against a {(int)Math.Ceiling(mission.EstimatedDuration.TotalMinutes)}m predicted calendar requirement.",
                TaskId = mission.TaskId, ProjectId = mission.ProjectId, ExternalId = mission.SourceRecordId ?? "",
                BusinessValue = mission.BusinessValue, Outcome = "Successful", Significance = 85
                , RecommendationId = _activeRecommendationId,
                CorrelationId = _activeCorrelationId ?? Guid.NewGuid(), ParentEventId = _activeDecisionEventId,
                CausedByEventId = _activeDecisionEventId,
                Recommendation = _activeRecommendationId is null ? "" : $"Start {mission.Title}",
                UserDecision = _activeRecommendationId is null ? "" : "Accepted"
            });
            _activeRecommendationId = null;
            _activeCorrelationId = null;
            _activeDecisionEventId = null;

            await memoryRepository.AddAsync(
                new GuardianMemory
                {
                    Category = "MissionCompleted",
                    Importance = 75,
                    Source = "User",

                    Summary =
                        $"Completed mission '{mission.Title}'.",

                    Detail = $"""
                        Duration: {elapsed:hh\:mm\:ss}
                        Estimated duration: {mission.EstimatedDuration}
                        Score: {mission.Score}
                        Business value: {mission.BusinessValue:C}
                        Source type: {mission.SourceType}
                        Source record: {mission.SourceRecordId ?? "None"}
                        Task ID: {mission.TaskId?.ToString() ?? "None"}
                        Project ID: {mission.ProjectId?.ToString() ?? "None"}
                        Completed at: {DateTime.Now:dd MMM yyyy HH:mm}
                        """,

                    TaskId = mission.TaskId,
                    ProjectId = mission.ProjectId
                });
            if (string.Equals(
        mission.SourceType,
        "Project",
        StringComparison.OrdinalIgnoreCase)
    && mission.ProjectId is not null)
            {
                await UpdateProjectAfterMissionAsync(
                    mission.ProjectId.Value);
            }

            var calendarRecovery = await ReconcileCompletedCalendarMissionAsync(mission, DateTimeOffset.Now);
            GuardianResponse =
                $"Mission completed: {mission.Title}." +
                (string.IsNullOrWhiteSpace(calendarRecovery) ? string.Empty : $" {calendarRecovery}");

            ChatHistory.Add(
                new()
                {
                    Role = "assistant",
                    Content = GuardianResponse
                });

            GuardianPanelExpanded = true;

            ExitMission();

            if (mission.ProjectId is not null)
            {
                await OfferProjectCompletionAsync(
                    mission.ProjectId.Value);
            }

            await _workspaceCoordinator.RefreshAsync();
            await RefreshCalendarAwareObjectiveAsync();
            _ = SpeakGuardianAsync(
                $"Mission complete. {mission.Title}. {Workspace.Briefing.GuardianComment}");
            await RefreshAnalyticsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Mission Completion Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task BackfillWorkedObjectiveToCalendarAsync(
        Nekomata.Models.Missions.Mission mission, DateTime startedAt, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromMinutes(1) ||
            mission.SourceType.Equals("Calendar", StringComparison.OrdinalIgnoreCase) ||
            mission.Status.Contains("SCHEDULED", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var segments = MissionWorkSegmentBuilder.Build(
                _missionWorkSegments,
                MissionPaused ? null : _missionSegmentStartedAt,
                DateTime.Now,
                startedAt,
                elapsed);

            var service = _services.GetRequiredService<ICalendarService>();
            var objectiveIdentity = mission.TaskId?.ToString() ?? mission.SourceRecordId ?? mission.Title;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var marker = $"NEKOMATA:WORKED:{segment.Start:yyyyMMddHHmmss}:{index}:{objectiveIdentity}";
                await service.CreateFocusEventAsync(mission.Title, segment.Start, segment.End, marker);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Worked-objective calendar backfill failed: {ex}");
        }
    }

    private IReadOnlyList<MissionWorkSegment> CaptureMissionWorkSegments(DateTime startedAt, TimeSpan elapsed) =>
        MissionWorkSegmentBuilder.Build(
            _missionWorkSegments,
            MissionPaused ? null : _missionSegmentStartedAt,
            DateTime.Now,
            startedAt,
            elapsed);
}
