using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class RecommendationBuilder
{
    public GuardianRecommendation Build(
        NekomataWorkspace workspace)
    {
        var recommendation = new GuardianRecommendation
        {
            Summary = "Guardian has analysed your workspace.",
            Text = workspace.CurrentMission.Title,
            Advice = new GuardianAdvice
            {
                Title = "Current Focus",
                Description = "Focus on the current mission."
            }
        };

        recommendation.Reasons.Add(
            $"Mission score: {workspace.CurrentMission.Score}");

        recommendation.Reasons.Add(
            $"Threat level: {workspace.CurrentMission.ThreatLevel}");

        return recommendation;
    }
}