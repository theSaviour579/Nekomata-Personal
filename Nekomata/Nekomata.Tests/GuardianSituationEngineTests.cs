using Nekomata.Core.Guardian.Situation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianSituationEngineTests
{
    [Fact]
    public void Healthy_loaded_day_stays_high_confidence()
    {
        var result = new GuardianSituationEngine().Build(new GuardianSituationInput
        {
            CalendarLoaded = true, CurrentObjective = "Purchasing forecast", NextDisruption = "14:30 Review"
        });

        Assert.Equal(100, result.DayConfidence);
        Assert.Empty(result.Signals);
        Assert.Contains("Purchasing forecast", result.StripText);
        Assert.Contains("14:30 Review", result.StripText);
    }

    [Fact]
    public void Multiple_weak_signals_reduce_confidence_and_preserve_evidence()
    {
        var result = new GuardianSituationEngine().Build(new GuardianSituationInput
        {
            CalendarLoaded = true, JudgementCount = 2, PriorityEmailCount = 2,
            OverdueTaskCount = 1, AtRiskProjectCount = 1, OverCapacityMinutes = 20
        });

        Assert.True(result.DayConfidence < 80);
        Assert.Contains(result.Signals, x => x.Source == "Attention");
        Assert.Contains(result.Signals, x => x.Source == "Calendar");
    }

    [Fact]
    public void Operational_threshold_is_a_high_significance_signal()
    {
        var result = new GuardianSituationEngine().Build(new GuardianSituationInput
        {
            CalendarLoaded = true, SystemNeedsAttention = true, SystemHealth = "Background email check failing"
        });

        Assert.Equal("System", Assert.Single(result.Signals).Source);
        Assert.Equal(85, result.Signals[0].Significance);
    }

    [Fact]
    public void Deferred_interruptions_remain_in_operating_context_without_becoming_judgements()
    {
        var result = new GuardianSituationEngine().Build(new GuardianSituationInput
        {
            CalendarLoaded = true, QueuedInterruptionCount = 2, BriefingSignalCount = 3
        });

        Assert.Equal(0, result.JudgementCount);
        Assert.Contains(result.Signals, x => x.Source == "Interruptions");
        Assert.Contains(result.Signals, x => x.Source == "Briefing");
        Assert.True(result.DayConfidence < 100);
    }

    [Fact]
    public void Operating_strip_exposes_distinct_now_next_and_later_states()
    {
        var result = new GuardianSituationEngine().Build(new GuardianSituationInput
        {
            CalendarLoaded = true,
            CurrentObjective = "Fallback objective",
            NowSummary = "Supplier review · 20m remaining",
            NextSummary = "11:00 Team meeting",
            LaterSummary = "45m at risk · recovery plan ready"
        });

        Assert.Contains("NOW · Supplier review · 20m remaining", result.StripText);
        Assert.Contains("NEXT · 11:00 Team meeting", result.StripText);
        Assert.Contains("LATER · 45m at risk · recovery plan ready", result.StripText);
    }
}
