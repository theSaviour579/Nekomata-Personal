using Nekomata.Core.Engines.Guardian;
using Nekomata.Core.Guardian;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public class GuardianEngine : IGuardianEngine
{
    private readonly MissionPriorityCalculator _priority;
    private readonly ConfidenceCalculator _confidence;
    private readonly RiskCalculator _risk;
    private readonly RecommendationBuilder _builder;

    public GuardianEngine(
        MissionPriorityCalculator priority,
        ConfidenceCalculator confidence,
        RiskCalculator risk,
        RecommendationBuilder builder)
    {
        _priority = priority;
        _confidence = confidence;
        _risk = risk;
        _builder = builder;
    }

    public GuardianState Analyse(NekomataWorkspace workspace)
    {
        var recommendation = _builder.Build(workspace);

        return new GuardianState
        {
            MissionScore = workspace.CurrentMission.Score,
            Confidence = _confidence.Calculate(workspace),
            RiskLevel = _risk.Calculate(workspace).Level,
            EstimatedValue = workspace.CurrentMission.BusinessValue,
            EstimatedDuration = workspace.CurrentMission.EstimatedDuration,
            StartBefore = workspace.CurrentMission.StartBefore,
            Summary = recommendation.Summary,
            Recommendation = recommendation.Text,
            Reasons = recommendation.Reasons,
            Advice = new List<GuardianAdvice>
{
    recommendation.Advice
}
        };
    }
}