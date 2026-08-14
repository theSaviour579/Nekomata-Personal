using Nekomata.Core.Guardian.Evidence;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianRiskBuilder
{
    public List<DecisionRisk> Build(
     GuardianEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var risks =
            new List<DecisionRisk>();

        var isOverCapacity =
            evidence.OverCapacity;

        System.Diagnostics.Debug.WriteLine(
            $"RiskBuilder OverCapacity = {isOverCapacity}");

        if (evidence.Overdue > 0)
        {
            risks.Add(new DecisionRisk
            {
                Title = "Overdue Tasks",
                Description =
                    evidence.Overdue == 1
                        ? "1 task is overdue."
                        : $"{evidence.Overdue} tasks are overdue.",
                Critical =
                    evidence.Overdue > 5
            });
        }

        if (evidence.Undated > 0)
        {
            risks.Add(new DecisionRisk
            {
                Title = "Missing Due Dates",
                Description =
                    evidence.Undated == 1
                        ? "1 task has no due date."
                        : $"{evidence.Undated} tasks have no due date.",
                Critical = false
            });
        }

        if (isOverCapacity)
        {
            risks.Add(new DecisionRisk
            {
                Title = "Capacity Exceeded",
                Description =
                    string.IsNullOrWhiteSpace(
                        evidence.CapacitySummary)
                        ? "Today's workload exceeds available capacity."
                        : evidence.CapacitySummary,
                Critical = true
            });
        }

        return risks;
    }
}