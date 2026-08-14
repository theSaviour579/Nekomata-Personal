using Nekomata.Models.Decision;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Engines;

public class DecisionEngine : IDecisionEngine
{
    public NekomataWorkspace Analyse(NekomataWorkspace workspace)
    {
        var task = workspace.Tasks.FirstOrDefault();

        if (task == null)
            return workspace;

        workspace.Recommendation.Summary =
            $"Focus on '{task.Title}' first.";

        if (task.BusinessCritical)
        {
            workspace.Recommendation.Reasons.Add(
                new DecisionReason
                {
                    Title = "Business Critical",
                    Explanation =
                        "This task directly affects business operations.",
                    ScoreContribution = 25
                });
        }

        if (task.AccuracySensitive)
        {
            workspace.Recommendation.Reasons.Add(
                new DecisionReason
                {
                    Title = "Accuracy Required",
                    Explanation =
                        "Mistakes could have a significant impact.",
                    ScoreContribution = 15
                });
        }

        if (task.DueAt != null)
        {
            workspace.Recommendation.Reasons.Add(
                new DecisionReason
                {
                    Title = "Due Soon",
                    Explanation =
                        $"Due {task.DueAt:g}",
                    ScoreContribution = 20
                });
        }

        if (!string.IsNullOrWhiteSpace(task.SuggestedDelegate))
        {
            workspace.Recommendation.DelegateTo =
                task.SuggestedDelegate;
        }
        else
        {
            workspace.Recommendation.NextAction =
                $"Begin {task.Title} now.";
        }

        return workspace;
    }
}