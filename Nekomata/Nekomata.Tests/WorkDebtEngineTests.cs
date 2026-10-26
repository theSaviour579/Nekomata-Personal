using Nekomata.Core.Analytics;
using Xunit;

namespace Nekomata.Tests;

public sealed class WorkDebtEngineTests
{
    [Fact]
    public void Repeated_similar_episodes_generate_emerging_debt()
    {
        var result = new WorkDebtEngine().Analyse(Episodes(3, 30, 10), 20.31m, new DateTime(2026, 9, 12));
        var debt = Assert.Single(result.Items);
        Assert.Equal(WorkDebtStatus.Emerging, debt.Status);
        Assert.Equal(3, debt.EpisodeCount);
    }

    [Fact]
    public void Cumulative_effort_above_remediation_becomes_actionable_and_deduplicated()
    {
        var episodes = Episodes(8, 60, 35).Concat(Episodes(2, 60, 35)).ToList();
        var debt = Assert.Single(new WorkDebtEngine().Analyse(episodes, 20.31m, new DateTime(2026, 9, 12)).Items);
        Assert.Equal(WorkDebtStatus.Actionable, debt.Status);
        Assert.Equal(8, debt.EpisodeCount);
    }

    [Theory]
    [InlineData("Weekly customer planning")]
    [InlineData("Manual weekly customer report")]
    public void Legitimate_recurring_administration_is_observation_only(string title)
    {
        var debt = Assert.Single(new WorkDebtEngine().Analyse(Episodes(10,60,35,title,"Customer service"),20m).Items);
        Assert.Equal(WorkDebtStatus.Observing,debt.Status);
        Assert.Contains("Repetition alone",debt.Recommendation);
        Assert.Contains($"{title} (10 episodes)", debt.EvidenceSummary);
        Assert.DoesNotContain("episode:0", debt.EvidenceSummary);
    }

    [Fact]
    public void Evidence_uses_readable_grouped_titles_instead_of_internal_mission_keys()
    {
        var episodes = Episodes(3, 30, 10, "Check backup PC", "Operational resilience", "Operational resilience");
        episodes[2] = episodes[2] with { Title = "Review failed backup" };

        var debt = Assert.Single(new WorkDebtEngine().Analyse(episodes, 20.31m).Items);

        Assert.Contains("Observed work: Check backup PC (2 episodes); Review failed backup.", debt.EvidenceSummary);
        Assert.DoesNotContain("episode:", debt.EvidenceSummary);
        Assert.DoesNotContain("mission:", debt.EvidenceSummary);
    }

    [Fact]
    public void Control_with_error_language_is_not_automatically_avoidable()
    {
        var debt = Assert.Single(new WorkDebtEngine().Analyse(Episodes(10,60,35,"Backup failure review"),20m).Items);
        Assert.Equal(WorkDebtStatus.Observing,debt.Status);
    }

    [Fact]
    public void One_off_work_does_not_become_debt() =>
        Assert.Empty(new WorkDebtEngine().Analyse(Episodes(1, 90, 1), 20.31m).Items);

    [Fact]
    public void Necessary_security_control_is_not_labelled_avoidable()
    {
        var items = Episodes(8, 30, 35, "Weekly security compliance backup review", "Security operations", "Operational resilience");
        var debt = Assert.Single(new WorkDebtEngine().Analyse(items, 20.31m).Items);
        Assert.Equal(WorkDebtStatus.Observing, debt.Status);
        Assert.Contains("necessary recurring control", debt.Recommendation);
    }

    [Fact]
    public void Annual_estimate_requires_sufficient_history()
    {
        Assert.Null(Assert.Single(new WorkDebtEngine().Analyse(Episodes(4, 30, 5), 20.31m).Items).EstimatedRecurringAnnualMinutes);
        Assert.NotNull(Assert.Single(new WorkDebtEngine().Analyse(Episodes(4, 30, 28), 20.31m).Items).EstimatedRecurringAnnualMinutes);
    }

    [Fact]
    public void Actionable_debt_builds_an_explainable_improvement_proposal()
    {
        var debt = Assert.Single(new WorkDebtEngine().Analyse(Episodes(8, 60, 35), 20.31m).Items);

        var proposal = WorkDebtImprovementProposalBuilder.Build(debt, 20.31m);

        Assert.NotNull(proposal);
        Assert.Equal(debt.EstimatedRemediationMinutes, proposal.EstimatedMinutes);
        Assert.Contains(debt.EvidenceSummary, proposal.Description);
        Assert.Contains("automation, delegation, documentation and accepted-cost", proposal.Description);
        Assert.True(proposal.EstimatedAnnualMinutesAvoided > 0);
    }

    [Fact]
    public void Observed_control_cannot_be_turned_into_an_improvement_task_without_actionable_evidence()
    {
        var debt = Assert.Single(new WorkDebtEngine().Analyse(
            Episodes(8, 30, 35, "Weekly security compliance backup review", "Security operations", "Operational resilience"), 20.31m).Items);

        Assert.Null(WorkDebtImprovementProposalBuilder.Build(debt, 20.31m));
    }

    private static List<WorkEpisode> Episodes(int count, int minutes, int spanDays, string title="Supplier portal chasing",
        string cluster="Supplier chasing", string category="Operational support") => Enumerable.Range(0,count).Select(i =>
    {
        var start = new DateTime(2026, 8, 1).AddDays(count == 1 ? 0 : i * spanDays / (count - 1d));
        return new WorkEpisode($"episode:{i}",title,category,start,start.AddMinutes(minutes),minutes,i%3==0?10:0,"Completed",0,
            ValueEvidenceKind.Proxy,50,"Operational",30,"Reactive work",cluster,null,null,[$"episode {i}"]);
    }).ToList();
}
