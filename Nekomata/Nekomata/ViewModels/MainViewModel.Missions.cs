using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
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

    [RelayCommand]
    private async Task BeginMissionAsync()
    {
        if (MissionActive)
            return;

        var mission = Workspace.CurrentMission;

        if (mission is null)
            return;

        _activeMission = mission;
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

        WorkspaceMode = Models.Workspace.WorkspaceMode.Mission;

        _missionTimer.Start();

        UpdateMissionTimer();

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

            MissionRemaining =
                $"+{overrun:hh\\:mm\\:ss}";

            MissionStateText =
                MissionPaused
                    ? "MISSION PAUSED"
                    : "MISSION OVERRUN";
        }
        else
        {
            MissionRemaining =
                remaining.ToString(@"hh\:mm\:ss");

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
        ChatInput = $"I am blocked on '{Workspace.CurrentMission.Title}'. Blocker: {reason} Replan the remainder of today around the live calendar, preserving protected meetings and urgent Halo work.";
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
                elapsed);

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

        MissionActive = false;
        _activeMission = null;
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

        MissionElapsed = "00:00:00";
        MissionRemaining = "00:00:00";

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

            if (mission.TaskId is not null)
            {
                var taskRepository =
                    _services.GetRequiredService<
                        ITaskRepository>();

                await taskRepository.CompleteAsync(
                    mission.TaskId.Value);
            }
            if (mission.SourceType.Equals("KnowBe4", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(mission.SourceRecordId))
            {
                var acknowledgements = _services.GetRequiredService<
                    Nekomata.Services.KnowBe4.KnowBe4AcknowledgementStore>();
                await acknowledgements.AcknowledgeAsync(mission.SourceRecordId);
                ResolveAttention($"knowbe4:failure:{mission.SourceRecordId}");
            }

            await _missionSessionService
                .RecordCompletedMissionAsync(
                    mission,
                    MissionStartedAt ?? DateTime.Now,
                    elapsed);

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

            GuardianResponse =
                $"Mission completed: {mission.Title}.";

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
}