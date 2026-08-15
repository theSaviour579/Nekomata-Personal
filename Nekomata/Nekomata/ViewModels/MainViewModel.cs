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
        };

        _clockTimer.Start();

        InitialiseIntegrationRefresh();
        InitialisePlanHealthMonitoring();
        InitialiseAttentionCentre();
        InitialiseDiagnosticsMonitoring();
        InitialiseReleaseSettings();

        _ = LoadAsync();
        _missionSimulationEngine = missionSimulationEngine;
    }

    public void ApplyPersonalProfile()
    {
        ApplicationName = "NEKOMATA PERSONAL";
        UpdateGreeting();
    }

    // ============================================================
    // WORKSPACE LIFECYCLE
    // ============================================================

    private void OnWorkspaceCoordinatorChanged(
        NekomataWorkspace updatedWorkspace)
    {
        updatedWorkspace.CurrentMission =
            ActiveMissionFocusPolicy.Resolve(
                MissionActive,
                _activeMission,
                updatedWorkspace.CurrentMission);

        Workspace = updatedWorkspace;

        // A rebuilt workspace contains its ranked default mission. Hide that
        // default until the live calendar has confirmed it is actionable.
        if (!MissionActive)
            ClearCalendarObjective();

        HandleUrgentHaloAlerts(updatedWorkspace);
        HandleKnowBe4Alerts(updatedWorkspace);
        ApplyGuardianCalendarGuidance();
        RefreshTimeAwareCapacity();

        TopRecommendation =
            _recommendationService
                .GetTopRecommendation(updatedWorkspace);

        OnPropertyChanged(nameof(GuardianSuggestedTasks));
        _ = RefreshCalendarAwareObjectiveAsync();
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
            OnPropertyChanged(nameof(GuardianSuggestedTasks));
            UpdateGreeting();

            TopRecommendation =
                _recommendationService.GetTopRecommendation(Workspace);

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
                _ = InitialiseSpotifyArrivalAsync();
            if (showSplash)
                _ = CheckForUpdatesAsync();
            if (showSplash)
                _ = SpeakMorningBriefingAsync();
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
                IsInitialLoading = false;
            }
        }
    }
    [RelayCommand]
    private async Task RefreshHaloTicketsAsync()
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
        var window =
            new MissionAnalysisWindow(this);

        if (Application.Current.MainWindow is not null)
        {
            window.Owner =
                Application.Current.MainWindow;
        }

        window.ShowDialog();
    }

    [ObservableProperty]
    private MissionSimulation? selectedSimulation;

    [RelayCommand]
    private void SimulateMission(
     GuardianRejectedMission rejectedMission)
    {
        ArgumentNullException.ThrowIfNull(rejectedMission);

        SelectedComparisonMission =
            rejectedMission;

        SelectedSimulation =
            _missionSimulationEngine.Simulate(
                Workspace.CurrentMission,
                rejectedMission,
                Workspace);
    }
    public IReadOnlyList<GuardianInsight> GuardianSuggestedTasks =>
    Workspace.Insights
        .Where(insight =>
            insight.CanCreateMission &&
            insight.SuggestedMission is not null)
        .ToList();

    [RelayCommand]
    private void SwitchMission(
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
                    "David",

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
                Workspace);

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
