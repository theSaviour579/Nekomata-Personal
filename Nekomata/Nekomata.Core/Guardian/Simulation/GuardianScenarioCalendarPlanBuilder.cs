using Nekomata.AI.Models.Actions;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Simulation;

public static class GuardianScenarioCalendarPlanBuilder
{
    public static GuardianActionResponse Build(GuardianDayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (!scenario.CanPrepare)
            throw new InvalidOperationException("The selected scenario contains no calendar blocks.");

        var changes = scenario.Blocks.Select(block => new GuardianChange
        {
            Selected = true,
            EntityType = "Calendar",
            EntityId = block.TaskId,
            Property = "CreateFocusBlock",
            OldValue = string.Empty,
            NewValue = $"{block.Start:O}|{block.End:O}|{block.Title}",
            Reason = $"Decision simulation '{scenario.Title}'; {scenario.RiskLabel}. Fixed commitments remain protected.",
            Confidence = scenario.ConfidencePercent
        }).ToList();

        return new GuardianActionResponse
        {
            ActionType = "decision_simulation_plan",
            Confidence = scenario.ConfidencePercent,
            Message = $"{scenario.Title} is ready for calendar review. {scenario.FinishLabel}; {scenario.RiskLabel}. Nothing has been changed yet.",
            Changes = changes
        };
    }
}
