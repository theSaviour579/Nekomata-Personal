using Nekomata.Core.Engines;
using Nekomata.Core.Integrations;
using Nekomata.Core.Missions.Suggestions;
using Nekomata.Core.Planning;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Core.Guardian.Context;
using Nekomata.Data.Repositories;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Workspace;

public class WorkspaceBuilder : IWorkspaceBuilder
{
    private readonly ITaskRepository
        _taskRepository;

    private readonly IProjectRepository
        _projectRepository;

    private readonly IFocusEngine
        _focusEngine;

    private readonly ICapacityEngine
        _capacityEngine;

    private readonly IDailyPlanner
        _dailyPlanner;

    private readonly IBriefingEngine
        _briefingEngine;

    private readonly IDecisionEngine
        _decisionEngine;

    private readonly IMissionEngine
        _missionEngine;

    private readonly IGuardianEngine
        _guardianEngine;

    private readonly IntegrationCoordinator
        _integrationCoordinator;

    private readonly IntegrationMissionConverter
        _integrationMissionConverter;

    private readonly MissionTimelinePlanner
        _timelinePlanner;

    private readonly IEnumerable<ISuggestedMissionProvider>
        _suggestionProviders;
    private readonly IGuardianOutcomeEngine _outcomeEngine;
    private readonly GuardianWorkThreadCorrelationEngine _workThreadCorrelation;
    private readonly GuardianWorkThreadEnrichmentService _workThreadEnrichment;
    private readonly LearnedWorkflowEngine _learnedWorkflow;
    private readonly ContextSignalResolutionService _contextSignals;
    private readonly TeamsContextEvidenceService _teamsContext;

    public WorkspaceBuilder(
        ITaskRepository taskRepository,
        IFocusEngine focusEngine,
        ICapacityEngine capacityEngine,
        IDailyPlanner dailyPlanner,
        IBriefingEngine briefingEngine,
        IDecisionEngine decisionEngine,
        IMissionEngine missionEngine,
        IGuardianEngine guardianEngine,
        IProjectRepository projectRepository,
        IntegrationCoordinator integrationCoordinator,
        IntegrationMissionConverter integrationMissionConverter,
        MissionTimelinePlanner timelinePlanner,
        IEnumerable<ISuggestedMissionProvider> suggestionProviders,
        IGuardianOutcomeEngine outcomeEngine,
        GuardianWorkThreadCorrelationEngine workThreadCorrelation,
        GuardianWorkThreadEnrichmentService workThreadEnrichment,
        LearnedWorkflowEngine learnedWorkflow,
        ContextSignalResolutionService contextSignals,
        TeamsContextEvidenceService teamsContext)
    {
        _taskRepository =
            taskRepository;

        _focusEngine =
            focusEngine;

        _capacityEngine =
            capacityEngine;

        _dailyPlanner =
            dailyPlanner;

        _briefingEngine =
            briefingEngine;

        _decisionEngine =
            decisionEngine;

        _missionEngine =
            missionEngine;

        _guardianEngine =
            guardianEngine;

        _projectRepository =
            projectRepository;

        _integrationCoordinator =
            integrationCoordinator;

        _integrationMissionConverter =
            integrationMissionConverter;

        _timelinePlanner =
            timelinePlanner;

        _suggestionProviders =
            suggestionProviders;
        _outcomeEngine = outcomeEngine;
        _workThreadCorrelation = workThreadCorrelation;
        _workThreadEnrichment = workThreadEnrichment;
        _learnedWorkflow = learnedWorkflow;
        _contextSignals = contextSignals;
        _teamsContext = teamsContext;
    }

    public async Task<NekomataWorkspace> BuildAsync()
    {
        var workspace =
            new NekomataWorkspace();

        // =========================================================
        // LOAD CORE WORKSPACE DATA
        // =========================================================

        workspace.Tasks =
            await _taskRepository
                .GetOpenTasksAsync();

        workspace.Projects =
            await _projectRepository
                .GetAllAsync();

        // =========================================================
        // LOAD EXTERNAL INTEGRATIONS
        // =========================================================

        var integrationSnapshot =
            await _integrationCoordinator
                .LoadAsync();

        workspace.Integrations =
            integrationSnapshot.Integrations
                .ToList();

        foreach (var integration in workspace.Integrations)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Integration: {integration.Name} | " +
                $"Connected={integration.Connected} | " +
                $"Status={integration.Status} | " +
                $"Missions={integration.MissionCount} | " +
                $"Duration=" +
                $"{integration.RefreshDuration?.TotalMilliseconds:0}ms");
        }

        workspace.IntegrationMissionCandidates =
            integrationSnapshot.IntegrationMissions
                .Select(mission =>
                    _integrationMissionConverter
                        .Convert(mission))
                .ToList();

        // Build explainable cross-system context before downstream reasoning.
        await _workThreadCorrelation.CorrelateAsync(workspace);
        await _contextSignals.ResolveAsync(workspace,DateTimeOffset.Now);
        await _teamsContext.CorrelateAsync(workspace,DateTimeOffset.Now);
        workspace.WorkThreadSummaries = (await _workThreadEnrichment.BuildAsync(workspace, DateTimeOffset.Now)).ToList();
        await _learnedWorkflow.ObserveAsync(workspace, DateTimeOffset.Now);

        // Recommendation -> decision -> behaviour -> outcome -> learning.
        // Apply explainable historical corrections before scoring and planning.
        await _outcomeEngine.ApplyLearningAsync(workspace);

        System.Diagnostics.Debug.WriteLine(
            $"Integrations loaded " +
            $"{workspace.IntegrationMissionCandidates.Count} " +
            $"candidate(s) from " +
            $"{integrationSnapshot.SourceName}.");

        // =========================================================
        // BUILD WORKSPACE ANALYSIS
        // =========================================================

        workspace =
            _focusEngine.BuildFocus(
                workspace);

        workspace =
            _capacityEngine.Calculate(
                workspace);

        workspace =
            _dailyPlanner.BuildPlan(
                workspace);

        workspace =
            _decisionEngine.Analyse(
                workspace);

        workspace =
            _missionEngine.BuildMission(
                workspace);

        workspace.Guardian =
            _guardianEngine.Analyse(
                workspace);

        // =========================================================
        // GENERATE GUARDIAN INSIGHTS
        // =========================================================

        var insightContext =
            new SuggestedMissionContext(
                workspace);

        workspace.Insights.Clear();

        foreach (var provider in _suggestionProviders)
        {
            var insights =
                provider.GetInsights(
                    insightContext);

            workspace.Insights.AddRange(
                insights);

            System.Diagnostics.Debug.WriteLine(
                $"Guardian insight provider " +
                $"'{provider.Name}' generated " +
                $"{insights.Count} insight(s).");
        }

        workspace.Insights =
            workspace.Insights
                .GroupBy(
                    insight => insight.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .OrderByDescending(insight =>
                    GetSeverityOrder(
                        insight.Severity))
                .ThenByDescending(insight =>
                    insight.DetectedAt)
                .ToList();

        System.Diagnostics.Debug.WriteLine(
            $"Guardian generated " +
            $"{workspace.Insights.Count} unique insight(s).");

        // =========================================================
        // GENERATE BRIEFING
        // =========================================================

        workspace =
            await _briefingEngine
                .GenerateAsync(
                    workspace);

        // =========================================================
        // BUILD TIMELINE
        // =========================================================

        workspace.Timeline =
            _timelinePlanner
                .Build(workspace)
                .ToList();

        foreach (var item in workspace.Timeline)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Timeline: " +
                $"{item.TimeRangeFormatted} | " +
                $"{item.Title}");
        }

        return workspace;
    }

    private static int GetSeverityOrder(
        string? severity)
    {
        return severity?
            .Trim()
            .ToLowerInvariant() switch
        {
            "critical" => 4,
            "warning" => 3,
            "info" => 2,
            "success" => 1,
            _ => 0
        };
    }
}
