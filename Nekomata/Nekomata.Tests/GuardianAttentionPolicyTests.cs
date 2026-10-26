using Nekomata.Core.Guardian.Authority;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianAttentionPolicyTests
{
    [Fact]
    public void Routine_healthy_information_stays_out_of_attention() =>
        Assert.False(GuardianAttentionPolicy.Assess("Guardian", "Info", "Routine sync complete", "Everything is healthy.", "open_dashboard").RequiresJudgement);

    [Fact]
    public void Proposed_decision_requires_judgement() =>
        Assert.True(GuardianAttentionPolicy.Assess("Guardian", "Info", "Review owner", "Evidence attached.", "review_assignment").RequiresJudgement);

    [Fact]
    public void Ambiguous_request_requires_judgement() =>
        Assert.True(GuardianAttentionPolicy.Assess("Email", "Info", "Clarify request", "The requested scope is ambiguous.", "open_dashboard").RequiresJudgement);

    [Fact]
    public void Project_update_window_requires_judgement_but_routine_info_does_not()
    {
        Assert.True(GuardianAttentionPolicy.Assess("PROJECT UPDATE", "Info", "Website relaunch", "No reply since Monday.", "open_dashboard").RequiresJudgement);
        Assert.False(GuardianAttentionPolicy.Assess("Projects", "Info", "Website relaunch", "On track.", "open_dashboard").RequiresJudgement);
    }
}
