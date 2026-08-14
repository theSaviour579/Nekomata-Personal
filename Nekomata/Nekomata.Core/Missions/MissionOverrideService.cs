using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions;

public class MissionOverrideService
    : IMissionOverrideService
{
    private readonly GuardianMissionDecisionBuilder
        _decisionBuilder;

    private readonly IGuardianLearningService
    _learningService;

    public MissionOverrideService(
    GuardianMissionDecisionBuilder decisionBuilder,
    IGuardianLearningService learningService)
    {
        _decisionBuilder = decisionBuilder;
        _learningService = learningService;
    }

    public MissionOverrideResult Override(
        NekomataWorkspace workspace,
        GuardianRejectedMission alternative)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(alternative);

        var previousMission =
            workspace.CurrentMission;

        workspace.CurrentMission =
            new Mission
            {
                Title =
                    alternative.Title,

                Score =
                    alternative.Score,

                BusinessValue =
                    alternative.BusinessValue,

                EstimatedDuration =
                    TimeSpan.FromMinutes(
                        Math.Max(
                            alternative.EstimatedMinutes,
                            1)),

                Progress =
                    alternative.Progress,

                ThreatLevel =
                    alternative.ThreatLevel,

                TaskId =
                    alternative.TaskId,

                ProjectId =
                    alternative.ProjectId,

                SourceType =
                    alternative.SourceType,

                Status =
                    "READY",

                RecommendationReason =
                    "Selected manually."
            };

        workspace.CurrentMission.Decision =
            _decisionBuilder.Build(
                workspace.CurrentMission,
                workspace.RankedMissionCandidates,
                workspace.Guardian.Confidence);

        workspace.CurrentMission.Decision.Headline =
            $"Manually selected {alternative.Title}.";

        workspace.CurrentMission.Decision.Recommendation =
            "Selected manually by the user.";

        workspace.CurrentMission.Decision.Summary =
            $"The original Guardian recommendation " +
            $"'{previousMission.Title}' was overridden in favour of " +
            $"'{alternative.Title}'.";

        var learningRecord =
    _learningService.RecordOverride(
        previousMission,
        workspace.CurrentMission,
        workspace);
        System.Diagnostics.Debug.WriteLine(
    $"Guardian Learning: {learningRecord.InferredPreference}");

        return new MissionOverrideResult
        {
            Success = true,

            PreviousMission =
                previousMission,

            CurrentMission =
                workspace.CurrentMission,

            Message =
                $"Guardian switched today's objective to " +
                $"'{alternative.Title}'."
        };
    }
}