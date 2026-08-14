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
using Nekomata.Core.Integrations.Halo;
using Nekomata.Core.Meetings;
using Nekomata.Core.Missions;
using Nekomata.Core.Missions.Candidates;
using Nekomata.Core.Missions.Scoring;
using Nekomata.Core.Missions.Suggestions;
using Nekomata.Core.Missions.Suggestions.Rules;
using Nekomata.Core.Planning;
using Nekomata.Core.Workspace;
using Nekomata.Data.Database;
using Nekomata.Data.Repositories;
using Nekomata.Models.Planning;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.DependencyInjection;
using Nekomata.Services.Halo;
using Nekomata.Services.KnowBe4;
using Nekomata.UI.ViewModels;
using Nekomata.UI.Services;
using Nekomata.UI.Windows;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI;

public partial class App : Application
{
    private readonly IHost _host;

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
                var haloOptions = context.Configuration
                    .GetSection("Halo")
                    .Get<HaloOptions>() ?? new HaloOptions();
                services.AddSingleton(haloOptions);

                var knowBe4Options = context.Configuration
                    .GetSection("KnowBe4")
                    .Get<KnowBe4Options>() ?? new KnowBe4Options();
                services.AddSingleton(knowBe4Options);
                services.AddSingleton<KnowBe4AcknowledgementStore>();
                var microsoftGraphOptions =
                    context.Configuration.GetSection("MicrosoftGraph").Get<MicrosoftGraphOptions>()
                    ?? new MicrosoftGraphOptions();
                services.AddMicrosoftGraph(microsoftGraphOptions);

                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<GuardianSpeechService>();
                services.AddSingleton<SpotifyPlaybackService>();
                services.AddSingleton<IntegrationDiagnosticsService>();
                services.AddSingleton<DatabaseBackupService>();
                services.AddSingleton<StartupRegistrationService>();
                services.AddSingleton<UpdateCheckService>();
                services.AddSingleton<FirstRunService>();
                services.AddTransient<FirstRunWindow>();
                services.AddSingleton<IFocusEngine, FocusEngine>();
                services.AddSingleton<NekomataDbContext>();
                services.AddSingleton<DatabaseInitializer>();
                services.AddSingleton<IWorkspaceBuilder, WorkspaceBuilder>();
                services.AddSingleton<ITaskRepository, TaskRepository>();
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
                var openAiConfigured =
                    !string.IsNullOrWhiteSpace(context.Configuration["OpenAI:ApiKey"])
                    || !string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

                if (openAiConfigured)
                {
                    services.AddSingleton<IAIProvider, OpenAIProvider>();
                    services.AddSingleton<IStructuredAIProvider, OpenAIStructuredProvider>();
                }
                else
                {
                    services.AddSingleton<IAIProvider, UnconfiguredAIProvider>();
                    services.AddSingleton<IStructuredAIProvider, UnconfiguredAIProvider>();
                }
                services.AddSingleton<IProjectRepository, ProjectRepository>();
                services.AddSingleton<IGuardianAuditRepository, GuardianAuditRepository>();
                services.AddSingleton<GuardianUndoService>();
                services.AddTransient<GuardianActivityViewModel>();
                services.AddTransient<GuardianActivityWindow>();
                services.AddTransient<ProjectWindowViewModel>();
                services.AddTransient<ProjectWindow>();
                services.AddSingleton<
    IGuardianMemoryRepository,
    GuardianMemoryRepository>();
                services.AddSingleton<
    IGuardianRecommendationService,
    GuardianRecommendationService>();
                services.AddSingleton<
    IMissionSelector,
    MissionSelector>();

                services.AddSingleton<
                    IMissionFactory,
                    MissionFactory>();
                services.AddSingleton<
    IMissionCandidateProvider,
    TaskMissionCandidateProvider>();

                services.AddSingleton<
                    IMissionCandidateProvider,
                    ProjectMissionCandidateProvider>();

                services.AddSingleton<
    IMissionCandidateScorer,
    MissionCandidateScorer>();
                
                services.AddSingleton<IMissionSessionService, MissionSessionService>();

                services.AddSingleton<IMissionSessionRepository, MissionSessionRepository>();

                services.AddSingleton<IMissionAnalyticsService, MissionAnalyticsService>();

                services.AddSingleton<IGuardianDecisionEngine,
                      GuardianDecisionEngine>();

                services.AddSingleton<GuardianReasonBuilder>();
                services.AddSingleton<GuardianRiskBuilder>();
                services.AddSingleton<GuardianOpportunityBuilder>();
                services.AddSingleton<GuardianConfidenceBuilder>();
                services.AddSingleton<GuardianNarrativeBuilder>();

                services.AddSingleton<
    IGuardianEvidenceBuilder,
    GuardianEvidenceBuilder>();

                services.AddSingleton<GuardianMissionDecisionBuilder>();

                services.AddSingleton<MissionSimulationEngine>();
                services.AddSingleton<IMissionOverrideService,
    MissionOverrideService>();

                services.AddSingleton<
    IGuardianLearningService,
    GuardianLearningService>();

                services.AddSingleton<IntegrationCoordinator>();

                services.AddSingleton<
    IMissionCandidateProvider,
    IntegrationMissionCandidateProvider>();

                services.AddSingleton<IntegrationMissionConverter>();

                services.AddSingleton<GuardianReasoningEngine>();

                services.AddSingleton<MissionTimelinePlanner>();

                services.AddSingleton(
    new WorkingDaySettings());

                services.AddSingleton<
    ITimelineProvider,
    FixedScheduleProvider>();

                services.AddSingleton<MissionComparisonEngine>();

                services.AddSingleton<
    ISuggestedMissionProvider,
    SuggestedMissionProvider>();
                var haloConfigured =
                    Uri.TryCreate(
                        haloOptions.BaseUrl.TrimEnd('/') + "/",
                        UriKind.Absolute,
                        out var haloBaseUri)
                    && (haloBaseUri.Scheme == Uri.UriSchemeHttps ||
                        haloBaseUri.Scheme == Uri.UriSchemeHttp)
                    && !string.IsNullOrWhiteSpace(haloOptions.ClientId)
                    && !string.IsNullOrWhiteSpace(haloOptions.ClientSecret);

                if (haloConfigured)
                {
                    services.AddHttpClient<HaloAuthenticationService>(client =>
                    {
                        client.BaseAddress = haloBaseUri;
                        client.Timeout = TimeSpan.FromSeconds(30);
                    });

                    services.AddHttpClient<IHaloClient, RealHaloClient>(client =>
                    {
                        client.BaseAddress = haloBaseUri;
                        client.Timeout = TimeSpan.FromSeconds(30);
                    });
                }
                else
                {
                    services.AddSingleton<IHaloClient, FakeHaloClient>();
                }

                services.AddSingleton<IWorkspaceDataSource,
                    HaloWorkspaceDataSource>();

                if (!string.IsNullOrWhiteSpace(knowBe4Options.ApiKey) &&
                    Uri.TryCreate(knowBe4Options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var knowBe4BaseUri) &&
                    knowBe4BaseUri.Scheme == Uri.UriSchemeHttps)
                {
                    services.AddHttpClient<KnowBe4Client>(client =>
                    {
                        client.BaseAddress = knowBe4BaseUri;
                        client.Timeout = TimeSpan.FromSeconds(45);
                    });
                    services.AddSingleton<IWorkspaceDataSource, KnowBe4WorkspaceDataSource>();
                }

                services.AddSingleton<
                    ITimelineProvider,
                    GuardianMissionTimelineProvider>();

                services.AddSingleton<
                    IHaloMissionPolicy,
                    HaloMissionPolicy>();

                services.AddSingleton<
    ISuggestedMissionRule,
    MissingDueDatesSuggestionRule>();

                services.AddSingleton<
    IGuardianTaskPlanningService,
    GuardianTaskPlanningService>();

                services.AddSingleton<IGuardianConversationService,
    GuardianConversationService>();

                services.AddSingleton<IMeetingAnalysisService,
    MeetingAnalysisService>();

                services.AddSingleton<
    IGuardianApplyService,
    GuardianApplyService>();

                services.AddSingleton<
    IGuardianTaskMapper,
    GuardianTaskMapper>();

                services.AddSingleton<
    IGuardianProjectChangeHandler,
    GuardianProjectChangeHandler>();

                services.AddSingleton<
    IGuardianProjectChangeValidator,
    GuardianProjectChangeValidator>();

                services.AddSingleton<
    IGuardianTaskActionHandler,
    GuardianTaskActionHandler>();

                services.AddSingleton<
    IGuardianProjectActionHandler,
    GuardianProjectActionHandler>();

                services.AddSingleton<CalendarUndoService>();

                services.AddSingleton<
    IGuardianCalendarActionHandler,
    GuardianCalendarActionHandler>();

                services.AddSingleton<
    IGuardianActionPipeline,
    GuardianActionPipeline>();

                services.AddSingleton<
    IDailyCapacityCalculator,
    DailyCapacityCalculator>();
            })
    .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await _host.StartAsync();

            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var database = _host.Services.GetRequiredService<DatabaseInitializer>();
            await database.InitialiseAsync(startupTimeout.Token);
        }
        catch (Exception ex)
        {
            _host.Services.GetService<ILogger<App>>()?.LogError(
                ex,
                "Nekomata started without its database");

            MessageBox.Show(
                "Nekomata could not connect to its database. The application will open, " +
                "but workspace data will be unavailable until the database settings or service are fixed.\n\n" +
                ex.Message,
                "Database unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

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
        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}
