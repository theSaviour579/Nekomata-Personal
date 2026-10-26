using Nekomata.Core.Analytics;
using Nekomata.Core.Planning;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public sealed class RolePlanningPolicyTests
{
    [Fact]
    public void CreatesCorrectivePlanFromRoleDrift()
    {
        var alignment = Alignment(strategic: 10, technical: 35, reactive: 20) with
        {
            PersistentUnderweightKeys = new HashSet<string> { "leadership" }
        };

        var plan = new RolePlanningPolicy().Create(alignment, DateTime.Today);

        Assert.True(plan.ReserveStrategicBlock);
        Assert.True(plan.ProtectLeadershipTime);
        Assert.True(plan.IncreaseDelegationPressure);
    }

    [Fact]
    public void BiasesStrategyAndLeadershipWithoutOverridingUrgency()
    {
        var policy = new RolePlanningPolicy();
        var plan = new RolePlanningPlan(DateTime.Today, 70, true, true, true, []);
        var strategic = Candidate("Automate purchasing process", 50);
        var leadership = Candidate("Alex 1:1 coaching", 50);
        var delegatable = Candidate("Supplier support report", 60);
        delegatable.CurrentOwner = "Alex"; delegatable.SuggestedOwner = "Alex"; delegatable.AssignmentConfidence = 78;
        var p1 = Candidate("Urgent supplier incident", 200); p1.IsUrgent = true;

        var ranked = policy.Apply(plan, [strategic, leadership, delegatable, p1]);

        Assert.Equal(12, strategic.RoleAlignmentScoreAdjustment);
        Assert.Equal(10, leadership.RoleAlignmentScoreAdjustment);
        Assert.Equal(-12, delegatable.RoleAlignmentScoreAdjustment);
        Assert.Equal(0, p1.RoleAlignmentScoreAdjustment);
        Assert.Same(p1, ranked[0]);
        Assert.Contains(strategic.ScoreFactors, factor => factor.Category == "Role Alignment");
    }

    [Fact]
    public void ApplyingPlanTwiceIsIdempotent()
    {
        var policy = new RolePlanningPolicy();
        var candidate = Candidate("Strategic transformation", 50);
        var plan = new RolePlanningPlan(DateTime.Today, 70, true, false, false, []);

        policy.Apply(plan, [candidate]);
        policy.Apply(plan, [candidate]);

        Assert.Equal(62, candidate.Score);
        Assert.Single(candidate.ScoreFactors, factor => factor.Category == "Role Alignment");
        Assert.Equal(1, candidate.RecommendationReason.Split("Role alignment:").Length - 1);
    }

    private static MissionCandidate Candidate(string title, int score) => new()
    {
        Title = title, Description = title, SourceType = "Task", Score = score, EstimatedMinutes = 60
    };

    private static RoleAlignmentSummary Alignment(double strategic, double technical, double reactive)
    {
        var values = new Dictionary<string, double> { ["strategic"] = strategic, ["technical"] = technical, ["reactive"] = reactive };
        var bands = new RoleAlignmentSettings().Targets.Select(target =>
        {
            var actual = values.GetValueOrDefault(target.Key, target.MinimumPercent);
            var status = actual < target.MinimumPercent ? "Below target" : actual > target.MaximumPercent ? "Above target" : "Within range";
            return new RoleAlignmentBand(target.Key, target.Name, 0, actual, target.MinimumPercent, target.MaximumPercent, status, "");
        }).ToList();
        return new("Your role", 70, 480, bands, [], []);
    }
}
