using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public class MissionEngine : IMissionEngine
{
    private readonly IMissionSelector _selector;
    private readonly IMissionFactory _factory;
    private readonly GuardianMissionDecisionBuilder
    _decisionBuilder;

    public MissionEngine(
    IMissionSelector selector,
    IMissionFactory factory,
    GuardianMissionDecisionBuilder decisionBuilder)
    {
        _selector = selector;
        _factory = factory;
        _decisionBuilder = decisionBuilder;
    }

    public NekomataWorkspace BuildMission(
    NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var rankedCandidates =
            _selector.Rank(workspace);

        foreach (var candidate in rankedCandidates.Take(30))
        {
            System.Diagnostics.Debug.WriteLine(
                $"Rank #{candidate.Rank}: " +
                $"[{candidate.SourceType}] " +
                $"{candidate.Title} " +
                $"Score={candidate.Score}");
        }

        workspace.RankedMissionCandidates =
            rankedCandidates.ToList();

        var winner =
            rankedCandidates.FirstOrDefault();

        if (winner is null)
        {
            workspace.CurrentMission.Title =
                "No available work items";

            workspace.CurrentMission.Status =
                "NO MISSION";

            workspace.CurrentMission.TaskId = null;
            workspace.CurrentMission.ProjectId = null;
            workspace.CurrentMission.SourceType = "None";

            return workspace;
        }

        workspace.CurrentMission =
            _factory.Create(winner);

        workspace.CurrentMission.Decision =
    _decisionBuilder.Build(
        workspace.CurrentMission,
        workspace.RankedMissionCandidates,
        workspace.Guardian.Confidence);

        return workspace;
    }
}