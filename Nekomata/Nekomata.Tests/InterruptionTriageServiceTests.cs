using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public class InterruptionTriageServiceTests
{
    private static readonly Mission Active = new() { Title = "YoY Spend Deep Dive" };

    [Fact]
    public void Triage_SwitchesForBlockingIdentityIssue()
    {
        var result = new InterruptionTriageService().Triage("Rhys - Entra issue - needs looking at today", Active, 142);
        Assert.Equal("Urgent", result.Decision);
        Assert.Contains("142 mins remaining", result.Explanation);
    }

    [Fact]
    public void Triage_QueuesOrdinaryWalkUpRequest()
    {
        var result = new InterruptionTriageService().Triage("Can you review this report later?", Active, 74);
        Assert.Equal("Queued", result.Decision);
        Assert.Contains("continue", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Triage_RecognisesExplicitDelegation()
    {
        var result = new InterruptionTriageService().Triage("Pass to Alex to check the licence list", Active, 74);
        Assert.Equal("Delegated", result.Decision);
    }
}
