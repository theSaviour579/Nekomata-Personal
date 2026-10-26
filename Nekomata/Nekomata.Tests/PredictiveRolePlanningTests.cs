using Nekomata.Core.Analytics;
using Nekomata.Core.Planning;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public sealed class PredictiveRolePlanningTests
{
    [Fact]
    public void PersistentStrategicShortfallStrengthensBias()
    {
        var engine = new RoleBiasEngine();
        var oneWeek = engine.Calculate(Summary(strategic: 10), [], currentWeekStarting: new DateTime(2026, 9, 7));
        var persistent = engine.Calculate(Summary(strategic: 10),
            [Summary(strategic: 10), Summary(strategic: 10), Summary(strategic: 10)],
            currentWeekStarting: new DateTime(2026, 9, 7));
        Assert.True(persistent.Single(x => x.BandKey == "strategic").Strength > oneWeek.Single(x => x.BandKey == "strategic").Strength);
        Assert.Equal(4, persistent.Single(x => x.BandKey == "strategic").ConsecutiveWeeks);
    }

    [Fact]
    public void RecoveredAlignmentCausesGradualBiasDecay()
    {
        var bias = new RoleBiasEngine().Calculate(Summary(strategic: 30), [], storedHistory:
            [new(new DateTime(2026, 8, 31), "strategic", "Underweight", 70, "persistent")],
            currentWeekStarting: new DateTime(2026, 9, 7)).Single(x => x.BandKey == "strategic");
        Assert.Equal("Decaying", bias.Direction);
        Assert.Equal(50, bias.Strength);
    }

    [Fact]
    public void UrgentIncidentDoesNotReceiveRoleProtectionScoreAdjustment()
    {
        var plan = new RolePlanningPolicy().Create(Summary(strategic: 10), DateTime.Now,
            [new("strategic", "Strategic", "Underweight", 90, 5, 15, "persistent", [])], null, null);
        var p1 = new MissionCandidate { Title = "Critical incident", SourceType = "Planner", IsUrgent = true, Score = 80 };
        var result = new RolePlanningPolicy().Apply(plan, [p1]).Single();
        Assert.Equal(80, result.Score);
        Assert.Equal(0, result.RoleAlignmentScoreAdjustment);
    }

    [Fact]
    public void UnavoidableMeetingCancellationCreatesOnlyLightLeadershipCorrection()
    {
        var current = Summary(strategic: 30, leadership: 5);
        var bias = new RoleBiasEngine().Calculate(current, [], new Dictionary<string, double> { ["leadership"] = .15 })
            .Single(x => x.BandKey == "leadership");
        Assert.InRange(bias.Strength, 1, 15);
        Assert.Contains("damped", bias.MainReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sparse_current_week_caps_role_bias_at_light_strength()
    {
        var current = new RoleAlignmentSummary("Your role", 0, 35,
            Summary(strategic: 0, leadership: 0, technical: 0).Bands, [], []);

        var biases = new RoleBiasEngine().Calculate(current,
            [Summary(strategic: 10), Summary(strategic: 10), Summary(strategic: 10)]);

        Assert.NotEmpty(biases);
        Assert.All(biases, bias => Assert.InRange(bias.Strength, 1, 29));
        Assert.All(biases, bias => Assert.Contains("capped at light", bias.MainReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompetingLeadershipAndStrategicShortfallsAreArbitrated()
    {
        var plan = new CorrectivePlanEngine().Build(Summary(strategic: 10, leadership: 8),
            [new("strategic", "Strategic", "Underweight", 80, 4, 15, "persistent", []),
             new("leadership", "Leadership", "Underweight", 55, 3, 7, "persistent", [])],
            240, 0, 0, null, 20);
        Assert.Equal(RoleCorrectionType.ReserveStrategicBlock, plan.MostImportant!.Type);
        Assert.InRange(plan.RankedActions.Count, 1, 3);
        Assert.True(plan.RecommendedMinutes <= 180);
    }

    [Fact]
    public void AcceptingProposedCorrectionChangesNoInterventionProjection()
    {
        var engine = new RoleAlignmentForecastEngine();
        var current = Summary(strategic: 10, technical: 70);
        var proposed = engine.Calculate(current, [], [new("strategic", 90, "proposal", ProjectedActivityDisposition.Proposed)]);
        var accepted = engine.Calculate(current, [], [new("strategic", 90, "accepted", ProjectedActivityDisposition.Accepted)]);
        Assert.NotEqual(proposed.NoIntervention.Score, accepted.NoIntervention.Score);
        Assert.Equal(accepted.NoIntervention.Score, accepted.RecommendedPlan.Score);
    }

    [Fact]
    public void RejectedCorrectionIsExcludedFromBothForecasts()
    {
        var engine = new RoleAlignmentForecastEngine();
        var baseline = engine.Calculate(Summary(strategic: 10, technical: 70), [], []);
        var forecast = engine.Calculate(Summary(strategic: 10, technical: 70), [],
            [new("strategic", 90, "rejected", ProjectedActivityDisposition.Rejected)]);
        Assert.Equal(baseline.NoIntervention.Score, forecast.NoIntervention.Score);
        Assert.Equal(forecast.NoIntervention.Score, forecast.RecommendedPlan.Score);
    }

    [Fact]
    public void InterruptionRiskChoosesMoreSurvivableProtectedBlock()
    {
        var monday = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        var interruptions = new[]
        {
            new GuardianInterruption { InterruptedAt = monday.AddHours(15), Description = "call" },
            new GuardianInterruption { InterruptedAt = monday.AddHours(15).AddMinutes(10), Description = "incident" }
        };
        var selected = new InterruptionRiskEstimator().SelectBest(
            [new(monday.AddHours(9), monday.AddHours(10.5), 0), new(monday.AddHours(15), monday.AddHours(16.5), 2)],
            interruptions, [], 12);
        Assert.Equal(9, selected!.Start.Hour);
        Assert.True(selected.RiskPercent < 50);
    }

    [Fact]
    public void ForecastKeepsEstimatesDistinctFromObservedResults()
    {
        var forecast = new RoleAlignmentForecastEngine().Calculate(Summary(strategic: 20, technical: 50), [], []);
        Assert.False(forecast.Current.IsEstimate);
        Assert.True(forecast.NoIntervention.IsEstimate);
        Assert.True(forecast.RecommendedPlan.IsEstimate);
        Assert.Contains("OBSERVED", forecast.Current.Label);
        Assert.Contains("ESTIMATE", forecast.RecommendedPlan.Label);
    }

    private static RoleAlignmentSummary Summary(double strategic = 30, double leadership = 20, double technical = 15)
    {
        var values = new Dictionary<string, double>
        {
            ["strategic"] = strategic, ["leadership"] = leadership, ["oversight"] = 20,
            ["technical"] = technical, ["reactive"] = 5, ["admin"] = 5
        };
        var targets = new RoleAlignmentSettings().Targets;
        var bands = targets.Select(target =>
        {
            var actual = values[target.Key];
            var status = actual < target.MinimumPercent ? "Below target" : actual > target.MaximumPercent ? "Above target" : "Within range";
            return new RoleAlignmentBand(target.Key, target.Name, (int)Math.Round(actual * 10), actual,
                target.MinimumPercent, target.MaximumPercent, status, status);
        }).ToList();
        var deviation = bands.Sum(band => band.ActualPercent < band.MinimumPercent
            ? band.MinimumPercent - band.ActualPercent : band.ActualPercent > band.MaximumPercent ? band.ActualPercent - band.MaximumPercent : 0);
        return new("Your role", Math.Clamp((int)Math.Round(100 - deviation), 0, 100), 1000, bands, [], []);
    }
}
