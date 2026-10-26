using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public class GuardianOutcomeEngineTests
{
    [Fact]
    public void Analyse_LearnsExplainableDurationRatioFromComparableWork()
    {
        var history = Enumerable.Range(0, 8).Select(index => new MissionSession
        {
            Title = $"SQL data analysis {index}", SourceType = "Task", Completed = true,
            EstimatedDurationMinutes = 180, ActualDurationMinutes = 125,
            StartedAt = new DateTime(2026, 8, 1 + index, 8, 15, 0), FinishedAt = new DateTime(2026, 8, 1 + index, 10, 20, 0)
        }).ToList();

        var insight = GuardianOutcomeEngine.Analyse(history).Insights.Single(x => x.Kind == "DurationAccuracy");

        Assert.Equal("Analysis", insight.WorkType);
        Assert.Equal(8, insight.SampleSize);
        Assert.InRange(insight.ObservedRatio!.Value, .69, .70);
        Assert.Contains("below", insight.Summary);
        Assert.True(insight.ConfidencePercent >= 80);
        Assert.True(insight.IsPolicyActive);
        Assert.InRange(insight.AppliedFactor!.Value, .69, .70);
    }

    [Fact]
    public void Analyse_TreatsRepeatedDeferralAsAChallengeSignal()
    {
        var history = Enumerable.Range(0, 4).Select(index => new MissionSession
        {
            Title = "Supplier chase #5143", SourceType = "Planner", Completed = false, Cancelled = false,
            EstimatedDurationMinutes = 30, StartedAt = DateTime.Today.AddHours(13 + index % 2), FinishedAt = DateTime.Today.AddHours(14 + index % 2)
        }).ToList();

        var insight = GuardianOutcomeEngine.Analyse(history).Insights.Single(x => x.Kind == "RepeatedDeferral");

        Assert.Equal(4, insight.SampleSize);
        Assert.Contains("avoidance", insight.Explanation);
    }

    [Fact]
    public void PatternEngine_ActivatesEvidenceBackedTimePolicies()
    {
        var morning = Enumerable.Range(1, 12).Select(day => new MissionSession
        {
            Title = "SQL analysis", SourceType = "Task", Completed = true,
            EstimatedDurationMinutes = 90, ActualDurationMinutes = 70,
            StartedAt = new DateTime(2026, 7, day, 9, 0, 0), FinishedAt = new DateTime(2026, 7, day, 10, 10, 0)
        });
        var afternoon = Enumerable.Range(1, 12).Select(day => new MissionSession
        {
            Title = "Strategic project", SourceType = "Project", Completed = day % 3 != 0,
            EstimatedDurationMinutes = 90, ActualDurationMinutes = 90,
            StartedAt = new DateTime(2026, 7, day, 13, 30, 0), FinishedAt = new DateTime(2026, 7, day, 15, 0, 0)
        });
        var interruptions = Enumerable.Range(1, 8).Select(day => new Nekomata.Models.Guardian.GuardianInterruption
        {
            InterruptedAt = new DateTimeOffset(2026, 7, day, 14, 0, 0, TimeSpan.Zero), LostMinutes = 20
        }).ToList();

        var insights = GuardianPatternEngine.Analyse(morning.Concat(afternoon).ToList(), interruptions);

        var analysis = Assert.Single(insights, x => x.PolicyAction == "prefer_analysis_before_11");
        Assert.True(analysis.IsPolicyActive);
        var afternoonRisk = Assert.Single(insights, x => x.PolicyAction == "avoid_deep_afternoon");
        Assert.True(afternoonRisk.IsPolicyActive);
        Assert.InRange(afternoonRisk.ObservedRatio!.Value, .66, .67);
    }

    [Fact]
    public void InterruptionInsights_FindDisruptionWindows()
    {
        var history = Enumerable.Range(0, 4).Select(index => new Nekomata.Models.Guardian.GuardianInterruption
        {
            InterruptedAt = new DateTimeOffset(2026, 8, 4, 9, 5 + index * 10, 0, TimeSpan.Zero),
            WorkType = "Analysis", LostMinutes = 9
        }).ToList();
        var summary = new Nekomata.Models.Guardian.GuardianOutcomeSummary();
        GuardianOutcomeEngine.AddInterruptionInsights(summary, history);
        Assert.Contains(summary.Insights, x => x.Kind == "InterruptionWindow" && x.Summary.Contains("4.0 interruptions/hour"));
        Assert.Contains(summary.Insights, x => x.Kind == "FragmentationCost");
    }
}
