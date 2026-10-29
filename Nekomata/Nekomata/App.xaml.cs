using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nekomata.AI.Interfaces;
using Nekomata.AI.Providers;
using Nekomata.Core.Analytics;
using Nekomata.Core.Analytics.Capacity;
using Nekomata.Core.Engines;
using Nekomata.Core.Engines.Guardian;
using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Actions;
using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Guardian.Changes;
using Nekomata.Core.Guardian.Decisions;
using Nekomata.Core.Guardian.Evidence;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Core.Guardian.Reasoning;
using Nekomata.Core.Guardian.Recommendations;
using Nekomata.Core.Guardian.Simulation;
using Nekomata.Core.Guardian.Tasks;
using Nekomata.Core.Integrations;
using Nekomata.Core.Meetings;
using Nekomata.Core.Missions;
using Nekomata.Core.Missions.Candidates;
using Nekomata.Core.Missions.Scoring;
using Nekomata.Core.Missions.Suggestions;
using Nekomata.Core.Missions.Suggestions.Rules;
using Nekomata.Core.Planning;
using Nekomata.Core.Personalization;
using Nekomata.Core.Workspace;
using Nekomata.Data.Local;
using Nekomata.Data.Repositories;
using Nekomata.Models.Planning;
using Nekomata.Models.Analytics;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Copilot;
using Nekomata.UI.ViewModels;
using Nekomata.UI.Services;
using Nekomata.UI.Services.LocalStores;
using Nekomata.Core.Commitments;
using Nekomata.UI.Windows;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\NekomataPersonal-5F882F30-5168-4F37-BBAA-2C67C03E7CF8";
    private readonly IHost _host;
    private readonly Mutex _singleInstanceMutex = new(false, SingleInstanceMutexName);
    private bool _ownsSingleInstance;
    private bool _hostStarted;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider((context, options) =>
            {
                options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                options.ValidateOnBuild = true;
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddUserSecrets<App>();
            })
            .ConfigureServices((context, services) =>
            {
                var microsoftGraphOptions =
                    context.Configuration.GetSection("MicrosoftGraph").Get<MicrosoftGraphOptions>()
                    ?? new MicrosoftGraphOptions();
                services.AddMicrosoftGraph(microsoftGraphOptions);
                services.AddHttpClient<CopilotChatService>(client =>
                {
                    client.BaseAddress = new Uri("https://graph.microsoft.com/beta/");
                    client.Timeout = TimeSpan.FromMinutes(2);
                });
                services.AddSingleton(context.Configuration.GetSection("Costing").Get<LabourCostSettings>()
                                      ?? new LabourCostSettings());
                services.AddSingleton(TimeProvider.System);

                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<GuardianSpeechService>();
                services.AddSingleton<GuardianListeningService>();
                services.AddSingleton<SpotifyPlaybackService>();
                services.AddSingleton<IntegrationDiagnosticsService>();
                services.AddSingleton<WindowsCredentialStore>();
                services.AddSingleton<GuardianAttachmentService>();
                services.AddSingleton<StartupRegistrationService>();
                services.AddSingleton<UpdateCheckService>();
                services.AddSingleton<PersonalProfileService>();
                services.AddSingleton<PersonalRoleProfileService>();
                services.AddSingleton<PersonalValueService>();
                services.AddSingleton<TeamRoleSkillService>();
                services.AddSingleton<IUserIdentity, PersonalUserIdentity>();
                services.AddSingleton<PersonalSecretService>();
                services.AddSingleton<PersonalBackupService>();
                services.AddSingleton<FirstRunService>();
                services.AddTransient<FirstRunWindow>();
                services.AddSingleton<IFocusEngine, FocusEngine>();

                // Personal keeps everything in local files under %LocalAppData%\Nekomata Personal;
                // there is no database server to configure.
                services.AddSingleton<LocalWorkspaceStore>();
                services.AddSingleton<LocalDocumentStore>();
                services.AddSingleton<ITaskRepository, LocalTaskRepository>();
                services.AddSingleton<IProjectRepository, LocalProjectRepository>();
                services.AddSingleton<ITeamProfileRepository, LocalTeamProfileRepository>();
                services.AddSingleton<IGuardianAuditRepository, LocalGuardianAuditRepository>();
                services.AddSingleton<IGuardianMemoryRepository, LocalGuardianMemoryRepository>();
                services.AddSingleton<IMissionSessionRepository, LocalMissionSessionRepository>();
                services.AddSingleton<IWorkdayEventRepository, LocalWorkdayEventRepository>();
                services.AddSingleton<IGuardianInterruptionRepository, LocalGuardianInterruptionRepository>();
                services.AddSingleton<IGuardianPolicyRepository, LocalGuardianPolicyRepository>();
                services.AddSingleton<IGuardianDecisionOutcomeRepository, LocalGuardianDecisionOutcomeRepository>();
                services.AddSingleton<IGuardianRelationshipRepository, LocalGuardianRelationshipRepository>();
                services.AddSingleton<IGuardianOpenLoopRepository, LocalGuardianOpenLoopRepository>();
                services.AddSingleton<IGuardianSignalCheckpointRepository, LocalGuardianSignalCheckpointRepository>();
                services.AddSingleton<IGuardianChaseRepository, LocalGuardianChaseRepository>();
                services.AddSingleton<IGuardianWorkThreadRepository, LocalGuardianWorkThreadRepository>();
                services.AddSingleton<IGuardianCalendarEffortRepository, LocalGuardianCalendarEffortRepository>();
                services.AddSingleton<ILearnedWorkflowRepository, LocalLearnedWorkflowRepository>();
                services.AddSingleton<IContextSignalRepository, LocalContextSignalRepository>();
                services.AddSingleton<IWorkDebtRepository, LocalWorkDebtRepository>();
                services.AddSingleton<ICommitmentRepository, LocalCommitmentRepository>();
                services.AddSingleton<LocalLearningStateStore>();
                services.AddSingleton<IRoleBiasHistoryStore>(x => x.GetRequiredService<LocalLearningStateStore>());
                services.AddSingleton<IRoleCorrectionStore>(x => x.GetRequiredService<LocalLearningStateStore>());
                services.AddSingleton<IDecisionLearningStore>(x => x.GetRequiredService<LocalLearningStateStore>());

                services.AddSingleton<IWorkspaceBuilder, WorkspaceBuilder>();
                services.AddSingleton<ICapacityEngine, CapacityEngine>();
                services.AddSingleton<IBriefingEngine, BriefingEngine>();
                services.AddSingleton<IDailyPlanner, DailyPlanner>();
                services.AddSingleton<IDecisionEngine, DecisionEngine>();
                services.AddSingleton<IMissionEngine, MissionEngine>();
                services.AddSingleton<MissionPriorityCalculator>();
                services.AddSingleton<ConfidenceCalculator>();
                services.AddSingleton<RiskCalculator>();
                services.AddSingleton<RecommendationBuilder>();
                services.AddSingleton<IGuardianEngine, GuardianEngine>();
                services.AddSingleton<IWorkspaceCoordinator, WorkspaceCoordinator>();
                services.AddSingleton<PersonalAIProvider>();
                services.AddSingleton<IAIProvider>(provider => provider.GetRequiredService<PersonalAIProvider>());
                services.AddSingleton<IStructuredAIProvider>(provider => provider.GetRequiredService<PersonalAIProvider>());
                services.AddTransient<TeamSkillsViewModel>();
                services.AddTransient<TeamSkillsWindow>();
                services.AddSingleton<GuardianUndoService>();
                services.AddTransient<GuardianActivityViewModel>();
                services.AddTransient<GuardianActivityWindow>();
                services.AddTransient<GuardianInterruptionNotificationWindow>();
                services.AddTransient<ProjectWindowViewModel>();
                services.AddTransient<ProjectWindow>();
                services.AddSingleton<IGuardianRecommendationService, GuardianRecommendationService>();
                services.AddSingleton<IMissionSelector, MissionSelector>();
                services.AddSingleton<IMissionFactory, MissionFactory>();
                services.AddSingleton<IMissionCandidateProvider, TaskMissionCandidateProvider>();
                services.AddSingleton<IMissionCandidateProvider, ProjectMissionCandidateProvider>();
                services.AddSingleton<IMissionCandidateScorer, MissionCandidateScorer>();
                services.AddSingleton<IMissionSessionService, MissionSessionService>();
                services.AddSingleton<IMissionAnalyticsService, MissionAnalyticsService>();
                services.AddSingleton<WeeklyOvertimeService>();
                services.AddSingleton<ExecutiveValueEngine>();
                services.AddSingleton<WorkDebtEngine>();
                services.AddSingleton<CommitmentEngine>();
                services.AddSingleton(sp =>
                {
                    var jobTitle = sp.GetRequiredService<PersonalProfileService>().Current.JobTitle.Trim();
                    return new RoleAlignmentSettings { RoleName = jobTitle.Length > 0 ? jobTitle : "Your role" };
                });
                services.AddSingleton<RoleAlignmentEngine>();
                services.AddSingleton<RoleDriftCauseClassifier>();
                services.AddSingleton<RoleBiasEngine>();
                services.AddSingleton<InterruptionRiskEstimator>();
                services.AddSingleton<CorrectivePlanEngine>();
                services.AddSingleton<RoleAlignmentForecastEngine>();
                services.AddSingleton<CorrectionOutcomeEvaluator>();
                services.AddSingleton<RolePlanningPolicy>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianOpenLoopService>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianOpenLoopIngestionService>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianOpenLoopAgeService>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianChasePreparationService>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianChaseWorkflowService>();
                services.AddSingleton<Nekomata.Core.Guardian.OpenLoops.GuardianLoopClosureService>();
                services.AddSingleton<Nekomata.Core.Guardian.WorkThreads.GuardianWorkThreadCorrelationEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.WorkThreads.GuardianWorkThreadEnrichmentService>();
                services.AddSingleton<Nekomata.Core.Guardian.WorkThreads.GuardianCalendarThreadEnrichmentService>();
                services.AddSingleton<Nekomata.Core.Guardian.Learning.LearnedWorkflowEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Learning.GuardianProposalLearningPolicy>();
                services.AddSingleton<Nekomata.Core.Guardian.Learning.LearnedWorkflowPromotionService>();
                services.AddSingleton<Nekomata.Core.Guardian.Context.ContextSignalResolutionService>();
                services.AddSingleton<Nekomata.Core.Guardian.Context.ProjectCommunicationLoopService>();
                services.AddSingleton<Nekomata.Core.Guardian.Context.TeamsContextEvidenceService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.WorkdayJournalService>();
                services.AddSingleton<Nekomata.Core.Guardian.Authority.GuardianActionAuthorityEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Authority.IGuardianAutoExecutionNotifier,
                    Nekomata.UI.Services.DesktopGuardianAutoExecutionNotifier>();
                services.AddSingleton<Nekomata.Core.Guardian.Authority.GuardianDecisionOutcomeService>();
                services.AddSingleton<Nekomata.Core.Guardian.Situation.GuardianSituationEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Relationships.GuardianRelationshipIntelligenceService>();
                services.AddSingleton<Nekomata.Core.Guardian.Forecasting.GuardianWorkloadForecastEngine>();
                services.AddSingleton<GuardianDayScenarioEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Interruptions.GuardianInterruptionPolicy>();
                services.AddSingleton<Nekomata.Core.Guardian.Reconciliation.GuardianDayReconciliationService>();
                services.AddSingleton<Nekomata.Core.Guardian.Exceptions.GuardianExceptionManagementService>();
                services.AddSingleton<Nekomata.Core.Guardian.Briefing.GuardianExecutiveBriefingService>();
                services.AddSingleton<Nekomata.Core.Guardian.Briefing.GuardianContextBriefingService>();
                services.AddSingleton<Nekomata.Core.Guardian.Anticipation.AnticipatoryOperationsEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Anticipation.AnticipatedActionWorkflowService>();
                services.AddSingleton<Nekomata.Core.Guardian.Anticipation.OperationalIntentDetectionService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.InterruptionTriageService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.RoleProtectionService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.GuardianDayReviewService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.DailyActivityReconciliationEngine>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.GuardianRecoveryPlanner>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.GuardianPolicyAccountabilityService>();
                services.AddSingleton<Nekomata.Core.Guardian.Outcomes.IGuardianOutcomeEngine,
                    Nekomata.Core.Guardian.Outcomes.GuardianOutcomeEngine>();
                services.AddSingleton<IGuardianDecisionEngine, GuardianDecisionEngine>();
                services.AddSingleton<GuardianReasonBuilder>();
                services.AddSingleton<GuardianRiskBuilder>();
                services.AddSingleton<GuardianOpportunityBuilder>();
                services.AddSingleton<GuardianConfidenceBuilder>();
                services.AddSingleton<GuardianNarrativeBuilder>();
                services.AddSingleton<IGuardianEvidenceBuilder, GuardianEvidenceBuilder>();
                services.AddSingleton<GuardianMissionDecisionBuilder>();
                services.AddSingleton<MissionSimulationEngine>();
                services.AddSingleton<IMissionOverrideService, MissionOverrideService>();
                services.AddSingleton<IGuardianLearningService, GuardianLearningService>();
                services.AddSingleton<IntegrationCoordinator>();
                // Personal edition intentionally excludes IT service-desk and security-training sources.
                services.AddSingleton<IWorkspaceDataSource, MicrosoftTasksWorkspaceDataSource>();
                services.AddSingleton<IWorkspaceDataSource, MicrosoftGraphContextDataSource>();
                services.AddSingleton<IMissionCandidateProvider, IntegrationMissionCandidateProvider>();
                services.AddSingleton<IntegrationMissionConverter>();
                services.AddSingleton<GuardianReasoningEngine>();
                services.AddSingleton<MissionTimelinePlanner>();
                services.AddSingleton(serviceProvider =>
                    serviceProvider.GetRequiredService<PersonalProfileService>()
                        .CreateWorkingDaySettings());
                services.AddSingleton<ITimelineProvider, FixedScheduleProvider>();
                services.AddSingleton<MissionComparisonEngine>();
                services.AddSingleton<ISuggestedMissionProvider, SuggestedMissionProvider>();
                services.AddSingleton<ITimelineProvider, GuardianMissionTimelineProvider>();
                services.AddSingleton<ISuggestedMissionRule, MissingDueDatesSuggestionRule>();
                services.AddSingleton<IGuardianTaskPlanningService, GuardianTaskPlanningService>();
                services.AddSingleton<IGuardianConversationService, GuardianConversationService>();
                services.AddSingleton<IMeetingAnalysisService, MeetingAnalysisService>();
                services.AddSingleton<IGuardianApplyService, GuardianApplyService>();
                services.AddSingleton<IGuardianTaskMapper, GuardianTaskMapper>();
                services.AddSingleton<IGuardianProjectChangeHandler, GuardianProjectChangeHandler>();
                services.AddSingleton<IGuardianProjectChangeValidator, GuardianProjectChangeValidator>();
                services.AddSingleton<IGuardianTaskActionHandler, GuardianTaskActionHandler>();
                services.AddSingleton<IGuardianProjectActionHandler, GuardianProjectActionHandler>();
                services.AddSingleton<CalendarUndoService>();
                services.AddSingleton<IGuardianCalendarActionHandler, GuardianCalendarActionHandler>();
                services.AddSingleton<IGuardianActionPipeline, GuardianActionPipeline>();
                services.AddSingleton<IDailyCapacityCalculator, DailyCapacityCalculator>();
            })
    .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Nekomata.UI.Services.CrashLog.Install(this);
        base.OnStartup(e);

        try
        {
            _ownsSingleInstance = _singleInstanceMutex.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            _ownsSingleInstance = true;
        }

        if (!_ownsSingleInstance)
        {
            MessageBox.Show(
                "Nekomata Personal is already running. Check the taskbar or notification area for the existing window.",
                "Nekomata Personal",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var workspaceReady = true;
        try
        {
            await _host.StartAsync();
            _hostStarted = true;

            var localStore = _host.Services.GetRequiredService<LocalWorkspaceStore>();
            await localStore.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            workspaceReady = false;
            _host.Services.GetService<ILogger<App>>()?.LogError(
                ex,
                "Nekomata Personal started without its local workspace");

            MessageBox.Show(
                "Nekomata could not open its local workspace. The application will open, " +
                "but workspace data may be unavailable until the local file issue is fixed.\n\n" +
                ex.Message,
                "Workspace unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_host.Services.GetService<Nekomata.UI.ViewModels.MainViewModel>() is { } viewModel)
            viewModel.WorkspaceReady = workspaceReady;

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        var firstRun = _host.Services.GetRequiredService<FirstRunService>();
        if (firstRun.IsFirstRun)
        {
            var setup = _host.Services.GetRequiredService<FirstRunWindow>();
            setup.Owner = mainWindow;
            setup.ShowDialog();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_hostStarted)
            await _host.StopAsync();
        _host.Dispose();

        if (_ownsSingleInstance)
            _singleInstanceMutex.ReleaseMutex();
        _singleInstanceMutex.Dispose();

        base.OnExit(e);
    }
}
