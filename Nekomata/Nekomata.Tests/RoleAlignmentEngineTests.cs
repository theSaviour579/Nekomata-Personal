using Nekomata.Core.Analytics;
using Xunit;

namespace Nekomata.Tests;

public sealed class RoleAlignmentEngineTests
{
    [Fact]
    public void SeparatesValuableWorkFromRoleAppropriateWork()
    {
        var summary = Summary(
            Episode("Transform purchasing", "Strategic projects", "High-leverage work", 180),
            Episode("Lead team", "Team management", "High-leverage work", 60),
            Episode("Supplier query", "Operational support", "Reactive work", 180),
            Episode("Email admin", "Administration & communication", "Administrative overhead", 60));

        var result = RoleAlignmentEngine.Analyse(summary);

        Assert.Equal(480, result.RecordedMinutes);
        Assert.Equal(37.5, result.Bands.Single(x => x.Key == "strategic").ActualPercent);
        Assert.Equal("Above target", result.Bands.Single(x => x.Key == "reactive").Status);
        Assert.Contains(result.Insights, x => x.StartsWith("Reactive requests"));
        Assert.True(result.Score < 100);
    }

    [Fact]
    public void DetectsThreeConsecutiveWeeklyDirectionChanges()
    {
        var current = Summary(Episode("Current", "Strategic projects", "High-leverage work", 60));
        var history = new[]
        {
            Summary(Episode("R1", "Operational support", "Reactive work", 30), Episode("S1", "Strategic projects", "High-leverage work", 270)),
            Summary(Episode("R2", "Operational support", "Reactive work", 60), Episode("S2", "Strategic projects", "High-leverage work", 240)),
            Summary(Episode("R3", "Operational support", "Reactive work", 90), Episode("S3", "Strategic projects", "High-leverage work", 210))
        };

        var result = RoleAlignmentEngine.Analyse(current, priorWeeks: history);

        Assert.Contains(result.Trends, x => x.StartsWith("Reactive requests increased"));
    }

    [Fact]
    public void Sparse_week_is_labelled_as_an_early_signal_instead_of_a_conclusion()
    {
        var result = RoleAlignmentEngine.Analyse(Summary(Episode("Reactive request", "Operational support", "Reactive work", 35)));

        Assert.True(result.IsProvisional);
        Assert.False(result.HasSufficientEvidence);
        Assert.Contains("PROVISIONAL", result.ScoreLabel);
        Assert.All(result.Bands, band => Assert.Equal("Early signal", band.Status));
        Assert.Contains(result.Insights, insight => insight.Contains("early signal", StringComparison.OrdinalIgnoreCase));
    }

    private static ExecutiveValueSummary Summary(params WorkEpisode[] episodes) => new(
        DateTime.Today, DateTime.Today, episodes.Sum(x => x.ActiveMinutes), 0, 0, 0, [], episodes, [], [], []);

    private static WorkEpisode Episode(string title, string category, string mode, int minutes) => new(
        title, title, category, DateTime.Today, DateTime.Today.AddMinutes(minutes), minutes, 0, "Completed", 0,
        ValueEvidenceKind.Proxy, 40, "Medium", 50, mode, category, null, null, []);
}
