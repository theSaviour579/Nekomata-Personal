using Nekomata.Core.Guardian.Interruptions;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianInterruptionPolicyTests
{
    private readonly GuardianInterruptionPolicy _policy = new();
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Critical_incident_surfaces_during_focus()
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Guardian", Severity = "Critical", Title = "Background job outage",
            FocusActive = true, CurrentObjective = "Purchasing forecast"
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.Immediate, result.Route);
        Assert.Contains(result.Evidence, x => x.Contains("incident", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Learned_dislike_stops_a_high_item_interrupting_focus()
    {
        GuardianInterruptionSignal Signal(int adjustment) => new()
        {
            Source = "Email", Severity = "High", Title = "Reply needed on the thread",
            FocusActive = true, CurrentObjective = "Budget review", PreferenceAdjustment = adjustment
        };

        Assert.Equal(GuardianInterruptionRoute.Immediate, _policy.Evaluate(Signal(0), Now).Route);
        var demoted = _policy.Evaluate(Signal(-40), Now);
        Assert.NotEqual(GuardianInterruptionRoute.Immediate, demoted.Route);
        Assert.Contains(demoted.Evidence, x => x.Contains("Your preference", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Critical", "Printer queue stuck")]
    [InlineData("Info", "Email outage reported by three sites")]
    public void Learned_dislike_never_hides_critical_or_incident_items(string severity, string title)
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Email", Severity = severity, Title = title, PreferenceAdjustment = -40
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.Immediate, result.Route);
        Assert.Contains(result.Evidence, x => x.Contains("set aside", StringComparison.Ordinal));
    }

    [Fact]
    public void Learned_interest_lifts_an_ordinary_item_to_immediate_when_free()
    {
        var plain = _policy.Evaluate(new() { Source = "Email", Severity = "Info", Title = "Supplier note" }, Now);
        var liked = _policy.Evaluate(new() { Source = "Email", Severity = "Info", Title = "Supplier note", PreferenceAdjustment = 40 }, Now);

        Assert.NotEqual(GuardianInterruptionRoute.Immediate, plain.Route);
        Assert.Equal(GuardianInterruptionRoute.Immediate, liked.Route);
    }

    [Fact]
    public void Ordinary_project_update_waits_for_natural_break()
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Project update", Severity = "Info", Title = "Supplier supplied a progress note",
            FocusActive = true, CurrentObjective = "Monthly reporting"
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.NaturalBreak, result.Route);
        Assert.Contains("Monthly reporting", result.Explanation);
    }

    [Fact]
    public void Healthy_automatic_signal_is_suppressed()
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Guardian", Severity = "Info", Title = "Healthy",
            Detail = "No action required"
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.Suppress, result.Route);
    }

    [Fact]
    public void Low_urgency_project_context_is_retained_for_briefing()
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Project update", Severity = "Info", Title = "Routine progress received"
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.Briefing, result.Route);
    }

    [Fact]
    public void Near_deadline_halo_decision_surfaces_now()
    {
        var result = _policy.Evaluate(new()
        {
            Source = "Email", Severity = "Warning", Title = "Approval required",
            ActionKind = "review_halo", Deadline = Now.AddMinutes(20), FocusActive = true
        }, Now);

        Assert.Equal(GuardianInterruptionRoute.Immediate, result.Route);
        Assert.Equal(100, result.PriorityScore);
    }
}
