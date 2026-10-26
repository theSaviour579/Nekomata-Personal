using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Interfaces;
using Nekomata.Core.Analytics;
using Nekomata.Core.Analytics.Capacity;
using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Actions;
using Nekomata.Core.Guardian.Recommendations;
using Nekomata.Core.Guardian.Simulation;
using Nekomata.Core.Meetings;
using Nekomata.Core.Missions;
using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Workspace;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Nekomata.UI.Services;
using Nekomata.UI.Views;
using System.Windows;
using System.Windows.Threading;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // ============================================================
    // SERVICES
    // ============================================================

    private readonly IWorkspaceCoordinator _workspaceCoordinator;
    private readonly IAIProvider _aiProvider;
    private readonly IServiceProvider _services;
    private readonly GuardianSpeechService _guardianSpeech;
    private readonly PersonalProfileService _personalProfile;
    private Mission? _manualObjectiveOverride;

    private readonly IGuardianRecommendationService
        _recommendationService;

    private readonly IMissionSessionService
        _missionSessionService;

    private readonly IMissionAnalyticsService
        _analyticsService;

    private readonly IMissionSessionRepository
        _missionSessionRepository;

    private readonly MissionSimulationEngine
    _missionSimulationEngine;

    private readonly IMissionOverrideService
    _missionOverrideService;

    private readonly ITaskRepository _taskRepository;

    private readonly IGuardianConversationService
    _guardianConversationService;

    private readonly IGuardianApplyService
    _guardianApplyService;

    // ============================================================
    // TIMERS
    // ============================================================

    private readonly DispatcherTimer _missionTimer;
    private readonly DispatcherTimer _clockTimer;

    private TimeSpan _missionAccumulatedElapsed =
        TimeSpan.Zero;

    private DateTime? _missionSegmentStartedAt;

    // ============================================================
    // SHARED APPLICATION STATE
    // ============================================================

    [ObservableProperty]
    private string applicationName = "NEKOMATA PERSONAL";

    [ObservableProperty]
    private string greeting = "Welcome to your workspace.";
    [ObservableProperty]
    private bool isInitialLoading = true;

    [ObservableProperty]
    private string loadingStatus = "Starting Guardian services…";

    private bool _initialLoadCompleted;

    [ObservableProperty]
    private NekomataWorkspace workspace = new();

    [ObservableProperty]
    private DateTime currentDateTime = DateTime.Now;

    [ObservableProperty]
    private WorkspaceMode workspaceMode =
        WorkspaceMode.Dashboard;

    [ObservableProperty]
    private GuardianRejectedMission?
    selectedComparisonMission;

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public MainViewModel(
        IWorkspaceCoordinator workspaceCoordinator,
        IAIProvider aiProvider,
        IServiceProvider services,
        GuardianSpeechService guardianSpeech,
        IGuardianRecommendationService recommendationService,
        IMissionSessionService missionSessionService,
        IMissionAnalyticsService analyticsService,
        IMissionSessionRepository missionSessionRepository,
        MissionSimulationEngine missionSimulationEngine,
        IMissionOverrideService missionOverrideService,
        ITaskRepository taskRepository,
        IGuardianConversationService conversationService,
        IGuardianApplyService guardianApplyService,
        PersonalProfileService personalProfile)
    {
        _workspaceCoordinator = workspaceCoordinator;
        _aiProvider = aiProvider;
        _services = services;
        _guardianSpeech = guardianSpeech;
        _personalProfile = personalProfile;

        _recommendationService =
            recommendationService;

        _missionSessionService =
            missionSessionService;

        _analyticsService =
            analyticsService;

        _missionSessionRepository =
            missionSessionRepository;

        _missionSimulationEngine =
            missionSimulationEngine;

        _missionOverrideService =
            missionOverrideService;

        _taskRepository =
    taskRepository;

        _guardianConversationService =
    conversationService;

        _guardianApplyService =
    guardianApplyService;

        _workspaceCoordinator.WorkspaceChanged +=
            OnWorkspaceCoordinatorChanged;

        _missionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _missionTimer.Tick += (_, _) =>
            UpdateMissionTimer();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _clockTimer.Tick += (_, _) =>
        {
            CurrentDateTime = DateTime.Now;
            RefreshElapsedLabourCost(CurrentDateTime);
            EvaluateEndOfDayReview();
            EvaluateMorningBriefingBoundary();
            _ = EvaluateScheduledAwayStatusAsync();
        };

        _clockTimer.Start();

        InitialisePrivacy(); // before anything can call the AI
        InitialiseIntegrationRefresh();
        InitialisePlanHealthMonitoring();
        InitialiseAttentionCentre();
        InitialiseDiagnosticsMonitoring();
        InitialiseReleaseSettings();
        InitialiseMicrosoftAccount();
        InitialiseWelcomeBack();
        InitialiseMeetingCopilot();
        InitialiseHud();
        ApplyGuardianPreferences();
        InitialiseQuietMode();
        InitialiseTeamsPresence();
        InitialiseCallGuard();
        InitialiseWatches();
        InitialiseTeamsWatch();
        InitialiseScreenContext();
        InitialiseTimeTracking();
        InitialiseAutonomy();
        InitialiseLearning();
        InitialiseThoughts();
        InitialiseHome();
        InitialiseAutopilot();
        InitialisePromiseTasks();
        InitialisePreparedReplies();
        InitialiseDelegations();
        InitialiseMeetingFollowThrough();

        _ = LoadAsync();
        _missionSimulationEngine = missionSimulationEngine;
    }

    public void ApplyPersonalProfile()
    {
        ApplicationName = "NEKOMATA PERSONAL";
        UpdateGreeting();
        OnPropertyChanged(nameof(PreferredMediaProvider));
        OnPropertyChanged(nameof(PreferredMediaTitle));
        OnPropertyChanged(nameof(PreferredMediaActionLabel));
        OnPropertyChanged(nameof(SpotifyControlsVisibility));
        _ = InitialisePreferredMediaAsync();
    }

    // ============================================================
    // WORKSPACE LIFECYCLE
    // ============================================================

    private void OnWorkspaceCoordinatorChanged(
        NekomataWorkspace updatedWorkspace)
    {
        var promotedObjective = !MissionActive
            ? _manualObjectiveOverride ?? (CalendarObjectiveAvailable ? Workspace.CurrentMission : null)
            : null;
        if (promotedObjective is not null &&
            !IsPromotedObjectiveStillAvailable(promotedObjective, updatedWorkspace))
        {
            if (ReferenceEquals(promotedObjective, _manualObjectiveOverride))
                _manualObjectiveOverride = null;
            promotedObjective = null;
            CalendarObjectiveAvailable = false;
            CanBeginBriefingObjective = false;
        }
        updatedWorkspace.CurrentMission =
            ActiveMissionFocusPolicy.Resolve(
                MissionActive,
                _activeMission,
                updatedWorkspace.CurrentMission);
        if (promotedObjective is not null)
            updatedWorkspace.CurrentMission = promotedObjective;

        ApplyRolePlanningBias(updatedWorkspace, replaceCurrentMission: !MissionActive && promotedObjective is null);

        Workspace = updatedWorkspace;

        // Preserve the currently promoted objective while the asynchronous
        // calendar refresh decides whether a scheduled block should replace it.
        // Clearing here caused valid free-window objectives to flash briefly
        // and then disappear whenever an integration rebuilt the workspace.
        if (promotedObjective is not null)
        {
            SyncBriefingObjective(promotedObjective);
            CalendarObjectiveAvailable = true;
        }
        else if (!MissionActive)
        {
            // Show the ranked default immediately (including imported Microsoft tasks). The calendar
            // refresh can then promote it into the current free window or explain when it can run.
            ShowTopRankedObjective("Guardian is checking the live calendar before suggesting when to start it.");
        }

        ApplyGuardianCalendarGuidance();
        RefreshTimeAwareCapacity();

        TopRecommendation =
            _recommendationService
                .GetTopRecommendation(updatedWorkspace);

        OnPropertyChanged(nameof(GuardianSuggestedTasks));
        _ = RefreshCalendarAwareObjectiveAsync();
    }

    private static bool IsPromotedObjectiveStillAvailable(
        Mission objective,
        NekomataWorkspace workspace)
    {
        if (objective.SourceType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
            return true;

        if (objective.TaskId is long taskId)
            return workspace.Tasks.Any(task => task.Id == taskId && !task.Completed && !task.IsCompleted);

        if (!string.IsNullOrWhiteSpace(objective.SourceRecordId))
        {
            return workspace.IntegrationMissionCandidates.Any(candidate =>
                candidate.SourceType.Equals(objective.SourceType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.SourceRecordId, objective.SourceRecordId, StringComparison.OrdinalIgnoreCase));
        }

        return workspace.RankedMissionCandidates.Any(candidate =>
            candidate.SourceType.Equals(objective.SourceType, StringComparison.OrdinalIgnoreCase) &&
            candidate.Title.Equals(objective.Title, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadAsync()
    {
        var showSplash = !_initialLoadCompleted;
        if (showSplash)
        {
            IsInitialLoading = true;
            LoadingStatus = "Building your workspace…";
        }

        try
        {
            Workspace = await _workspaceCoordinator.RefreshAsync();
            await NormalizeLegacyPersonalNamesAsync();
            OnPropertyChanged(nameof(GuardianSuggestedTasks));
            UpdateGreeting();

            TopRecommendation =
                _recommendationService.GetTopRecommendation(Workspace);
            if (showSplash)
                _ = RecordDayStartedAsync();

            if (showSplash)
                LoadingStatus = "Loading mission history and today’s progress…";
            await RefreshAnalyticsAsync();

            if (showSplash)
                LoadingStatus = "Reading today’s calendar…";
            await RefreshDailyBriefingContextAsync();
            await RefreshCalendarAwareObjectiveAsync();

            if (showSplash)
                LoadingStatus = "Guardian is ready.";
            if (showSplash)
                _ = InitialisePreferredMediaAsync();
            if (showSplash)
                _ = CheckForUpdatesAsync();
            if (showSplash)
                _ = RefreshInterruptionsAsync();
            if (showSplash)
            {
                // Record that today's briefing has played, so the day-boundary check doesn't
                // repeat it once loading finishes and Welcome back knows the day has begun.
                MarkMorningBriefingAnnounced();
                _ = SpeakMorningBriefingWithWeeklyAsync();
            }
        }
        catch (Exception ex) when (!WorkspaceReady)
        {
            Think($"Waiting for the local workspace: {ex.GetBaseException().Message}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Nekomata Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            if (showSplash)
            {
                await Task.Delay(350);
                _initialLoadCompleted = true;
                _ = BootSystemsCheckAsync();
                IsInitialLoading = false;
            }
        }
    }
    [RelayCommand]
    private async Task RefreshWorkspaceAsync()
    {
        await LoadAsync();
        _ = RefreshDiagnosticsAsync();
    }

    public IEnumerable<MissionCandidate> AlternativeMissions =>
       Workspace.RankedMissionCandidates
           .Where(candidate =>
               candidate.TaskId != Workspace.CurrentMission.TaskId ||
               candidate.ProjectId != Workspace.CurrentMission.ProjectId ||
               !string.Equals(
                   candidate.SourceType,
                   Workspace.CurrentMission.SourceType,
                   StringComparison.OrdinalIgnoreCase))
           .Take(5);

    [RelayCommand]
    private void OpenMissionAnalysis()
    {
        // Calendar-aware promotion and several manual start paths create a fresh
        // Mission after the workspace engine has built its decision. Rebuild the
        // comparison here so analysis always reflects the live ranked candidates.
        var analysisMission = Workspace.CurrentMission;
        analysisMission.Decision = _services
            .GetRequiredService<GuardianMissionDecisionBuilder>()
            .Build(
                analysisMission,
                Workspace.RankedMissionCandidates,
                Workspace.Guardian.Confidence);

        MissionAnalysisMission = analysisMission;
        SelectedComparisonMission = analysisMission.Decision
            .RejectedMissions
            .OrderBy(item => item.Rank <= 0 ? int.MaxValue : item.Rank)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();

        var window =
            new MissionAnalysisWindow(this);

        if (Application.Current.MainWindow is not null)
        {
            window.Owner =
                Application.Current.MainWindow;
        }

        try
        {
            window.ShowDialog();
        }
        finally
        {
            MissionAnalysisMission = null;
            SelectedComparisonMission = null;
            SelectedSimulation = null;
        }
    }

    [ObservableProperty]
    private Mission? missionAnalysisMission;

    [ObservableProperty]
    private MissionSimulation? selectedSimulation;

    [RelayCommand]
    private void SimulateMission(
     GuardianRejectedMission rejectedMission)
    {
        ArgumentNullException.ThrowIfNull(rejectedMission);

        SelectedComparisonMission =
            rejectedMission;
    }

    partial void OnSelectedComparisonMissionChanged(GuardianRejectedMission? value)
    {
        if (value is not null && MissionAnalysisMission is not null)
            SelectedSimulation = _missionSimulationEngine.Simulate(MissionAnalysisMission, value, Workspace);
        else
            SelectedSimulation = null;
    }

    [RelayCommand]
    private async Task SwitchSelectedMissionAsync()
    {
        if (SelectedComparisonMission is not null)
            await SwitchMissionAsync(SelectedComparisonMission);
    }
    public IReadOnlyList<GuardianInsight> GuardianSuggestedTasks =>
    Workspace.Insights
        .Where(insight =>
            insight.CanCreateMission &&
            insight.SuggestedMission is not null)
        .ToList();

    [RelayCommand]
    private async Task SwitchMissionAsync(
       GuardianRejectedMission rejectedMission)
    {
        ArgumentNullException.ThrowIfNull(rejectedMission);

        var confirmation =
            MessageBox.Show(
                $"Replace today's objective with '{rejectedMission.Title}'?\n\n" +
                "Guardian will record this as a manual override.",
                "Switch Mission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
            return;

        // Preserve elapsed work and stop the old timer before changing the live
        // objective. Otherwise completion could be attributed to the replacement.
        if (MissionActive)
        {
            await HoldMissionForLaterAsync();
            if (MissionActive)
                return;
        }

        var overrideResult =
            _missionOverrideService.Override(
                Workspace,
                rejectedMission);

        if (!overrideResult.Success)
        {
            MessageBox.Show(
                overrideResult.Message,
                "Mission Override",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        _manualObjectiveOverride = overrideResult.CurrentMission;
        CalendarObjectiveAvailable = true;
        SyncBriefingObjective(overrideResult.CurrentMission!);
        MissionAnalysisMission = overrideResult.CurrentMission;
        SelectedComparisonMission = overrideResult.CurrentMission?.Decision.RejectedMissions
            .OrderBy(item => item.Rank <= 0 ? int.MaxValue : item.Rank)
            .ThenByDescending(item => item.Score)
            .FirstOrDefault();

        await RecordRecommendationDecisionAsync(
            $"Continue {overrideResult.PreviousMission?.Title ?? "Guardian's selected mission"}",
            "Rejected", $"Started {rejectedMission.Title}");

        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(AlternativeMissions));
        OnPropertyChanged(nameof(HasBriefingObjective));
        OnPropertyChanged(nameof(BriefingSummary));
        OnPropertyChanged(nameof(GuardianSuggestedTasks));

        SelectedSimulation = null;

        MessageBox.Show(
            overrideResult.Message,
            "Guardian",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task AddSuggestedTask(
    GuardianInsight insight)
    {
        ArgumentNullException.ThrowIfNull(insight);

        if (insight.SuggestedMission is null)
            return;

        var mission =
            insight.SuggestedMission;

        var task =
            new NekomataTask
            {
                Title =
                    mission.Title,

                Description =
                    mission.Description,

                Source =
                    "Guardian",

                Status =
                    "Open",

                Priority =
                    mission.Priority,

                Owner =
                    _personalProfile.Current.DisplayName,

                EstimatedMinutes =
                    mission.EstimatedMinutes,

                DueAt =
                    mission.DueAt,

                ProjectId =
                    mission.ProjectId,

                EstimatedBusinessValue =
                    mission.BusinessValue,

                Category =
                    insight.Category,

                Tags =
                    "Guardian",

                BusinessCritical =
                    mission.BusinessValue > 10000,

                AccuracySensitive =
                    false
            };

        await _taskRepository.SaveAsync(task);

        Workspace =
            await _workspaceCoordinator.RefreshAsync();

        OnPropertyChanged(nameof(GuardianSuggestedTasks));

        MessageBox.Show(
            "Guardian added the suggested task to your backlog.",
            "Guardian",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenGuardianPlanner()
    {
        var meetingAnalysisService =
            _services.GetRequiredService<IMeetingAnalysisService>();

        var viewModel =
            new GuardianTaskPlannerViewModel(
                meetingAnalysisService,
                Workspace,
                _services.GetRequiredService<IGuardianApplyService>(),
                async () =>
                {
                    Workspace = await _workspaceCoordinator.RefreshAsync();
                    OnPropertyChanged(nameof(GuardianSuggestedTasks));
                });

        var window =
            new GuardianTaskPlannerWindow
            {
                DataContext = viewModel
            };

        if (Application.Current.MainWindow is not null)
        {
            window.Owner =
                Application.Current.MainWindow;
        }

        window.ShowDialog();
    }

    private async Task UpdateProjectAfterMissionAsync(long projectId)
    {
        var projectRepository =
            _services.GetRequiredService<IProjectRepository>();

        var project =
            await projectRepository.GetByIdAsync(projectId);

        if (project is null)
            return;

        // Completing a project mission means the project work
        // represented by that mission has now been finished.
        project.ProgressPercent = 100;

        if (project.EstimatedRemainingMinutes > 0)
        {
            project.EstimatedRemainingMinutes = 0;
        }

        project.NextAction = string.Empty;

        await projectRepository.SaveAsync(project);
    }
}
