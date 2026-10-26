using Nekomata.Core.Missions;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public class AdaptiveReplanPolicyTests
{
    private static Mission Current => new() { SourceType = "Task", TaskId = 1, Title = "Current" };
    private static MissionCandidate Alternative => new() { SourceType = "Task", TaskId = 2, Title = "Alternative", EstimatedMinutes = 15, Score = 100 };
    [Fact]
    public void Higher_score_alone_does_not_interrupt_focus() => Assert.Null(AdaptiveReplanPolicy.Evaluate(Current, [Alternative], 20, 30, false));
    [Fact]
    public void Blocked_objective_suggests_fitting_work() => Assert.NotNull(AdaptiveReplanPolicy.Evaluate(Current, [Alternative], 20, 30, true));
    [Fact]
    public void Calendar_shortfall_suggests_smaller_objective() => Assert.NotNull(AdaptiveReplanPolicy.Evaluate(Current, [Alternative], 60, 20, false));
    [Fact]
    public void Alternatives_that_do_not_fit_are_excluded() => Assert.Null(AdaptiveReplanPolicy.Evaluate(Current, [Alternative], 60, 10, true));

    [Fact] public void Predicted_duration_excludes_an_underestimated_alternative()
    {
        var candidate = Alternative;
        candidate.EstimatedMinutes = 10;
        candidate.PredictedActiveMinutes = 45;
        Assert.Null(AdaptiveReplanPolicy.Evaluate(Current, [candidate], 60, 15, true));
    }
    [Fact]
    public void Waiting_work_is_not_a_focus_alternative()
    {
        var candidate = Alternative; candidate.IsAwaitingExternalResponse = true;
        Assert.Null(AdaptiveReplanPolicy.Evaluate(Current, [candidate], 60, 30, true));
    }
    [Fact]
    public void Urgent_fitting_work_can_trigger_suggestion()
    {
        var candidate = Alternative; candidate.RequiresImmediateAttention = true;
        Assert.NotNull(AdaptiveReplanPolicy.Evaluate(Current, [candidate], 20, 30, false));
    }
}
