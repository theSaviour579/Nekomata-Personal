using Nekomata.Core.Guardian.Forecasting;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianWorkloadForecastEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Forecast_includes_interruption_allowance_before_declaring_capacity_safe()
    {
        var result = new GuardianWorkloadForecastEngine().Build(new()
        {
            Now = Now, CalendarLoaded = true, RemainingWorkableMinutes = 300,
            FixedCommitmentMinutes = 60, PlannedWorkMinutes = 220, InterruptionAllowanceMinutes = 35,
            InterruptionEvidenceCount = 8
        });

        Assert.Equal(15, result.MinutesAtRisk);
        Assert.Equal("WATCH", result.StatusLabel);
        Assert.True(result.NeedsRecovery);
        Assert.True(result.ConfidencePercent >= 80);
    }

    [Fact]
    public void Forecast_identifies_lower_ranked_work_that_exceeds_resilient_capacity()
    {
        var result = new GuardianWorkloadForecastEngine().Build(new()
        {
            Now = Now, CalendarLoaded = true, RemainingWorkableMinutes = 180,
            FixedCommitmentMinutes = 30, PlannedWorkMinutes = 180, InterruptionAllowanceMinutes = 20,
            WorkItems =
            [
                new() { Title = "Critical incident", Minutes = 90, Score = 100, IsCritical = true },
                new() { Title = "Routine report", Minutes = 60, Score = 30 }
            ]
        });

        Assert.Contains("Routine report", result.ThreatenedCommitments);
        Assert.DoesNotContain("Critical incident", result.ThreatenedCommitments);
    }

    [Fact]
    public void Forecast_reports_resilience_when_plan_fits()
    {
        var result = new GuardianWorkloadForecastEngine().Build(new()
        {
            Now = Now, CalendarLoaded = true, RemainingWorkableMinutes = 300,
            FixedCommitmentMinutes = 60, PlannedWorkMinutes = 120
        });

        Assert.Equal("ON TRACK", result.StatusLabel);
        Assert.Equal(0, result.MinutesAtRisk);
        Assert.Contains("120 workable minutes", result.Summary);
    }
}
