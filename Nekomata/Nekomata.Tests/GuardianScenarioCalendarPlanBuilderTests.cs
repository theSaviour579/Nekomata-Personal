using Nekomata.Core.Guardian.Simulation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianScenarioCalendarPlanBuilderTests
{
    [Fact]
    public void Build_CreatesSelectedCalendarChangesWithoutApplyingThem()
    {
        var start = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(1));
        var scenario = new GuardianDayScenario
        {
            Title = "Protect selected objective",
            ConfidencePercent = 84,
            Blocks =
            [
                new GuardianScenarioBlock { TaskId = 42, Title = "Strategic review", Start = start, End = start.AddMinutes(90) }
            ]
        };

        var plan = GuardianScenarioCalendarPlanBuilder.Build(scenario);

        var change = Assert.Single(plan.Changes);
        Assert.Equal("decision_simulation_plan", plan.ActionType);
        Assert.True(change.Selected);
        Assert.Equal("Calendar", change.EntityType);
        Assert.Equal("CreateFocusBlock", change.Property);
        Assert.Equal(42, change.EntityId);
        Assert.Contains("Strategic review", change.NewValue);
        Assert.Contains("Nothing has been changed yet", plan.Message);
    }

    [Fact]
    public void Build_RejectsScenarioWithoutCalendarBlocks()
    {
        var scenario = new GuardianDayScenario { Title = "Empty" };

        var error = Assert.Throws<InvalidOperationException>(() => GuardianScenarioCalendarPlanBuilder.Build(scenario));

        Assert.Contains("no calendar blocks", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
