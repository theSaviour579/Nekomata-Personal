using Nekomata.Core.Analytics;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Missions;
using Xunit;

namespace Nekomata.Tests;

public sealed class RoleProtectionServiceTests
{
    [Fact]
    public void RecommendsNamedHandoffForRepeatedRoutineWorkDisplacingStrategy()
    {
        var result = RoleProtectionService.Evaluate(
            "Can you just pull these figures? About 45 minutes.",
            new Mission { Title = "Commission automation", Description = "Strategic transformation", Score = 90, BusinessValue = 25_000 },
            93, 5, "Alex", 78, Alignment(reactivePercent: 22), 75);

        Assert.True(result.Applies);
        Assert.Equal("Delegated", result.Decision);
        Assert.Equal("Alex", result.SuggestedDelegate);
        Assert.Contains("Nothing will be assigned until you approve", result.Explanation);
    }

    [Fact]
    public void UrgentIncidentAlwaysBypassesRoleProtection()
    {
        var result = RoleProtectionService.Evaluate(
            "Urgent outage - nobody can work", new Mission { Title = "Strategy", Score = 95 },
            90, 6, "Alex", 88, Alignment(25), 120);

        Assert.False(result.Applies);
    }

    [Fact]
    public void DoesNotChallengeWithoutEnoughEvidence()
    {
        var result = RoleProtectionService.Evaluate(
            "Please review this unusual architecture decision", new Mission { Title = "Routine task", Score = 20 },
            90, 0, "", 0, Alignment(5), 0);

        Assert.False(result.Applies);
    }

    private static RoleAlignmentSummary Alignment(double reactivePercent)
    {
        var bands = new RoleAlignmentSettings().Targets.Select(target => new RoleAlignmentBand(
            target.Key, target.Name, 0, target.Key == "reactive" ? reactivePercent : target.MinimumPercent,
            target.MinimumPercent, target.MaximumPercent,
            target.Key == "reactive" && reactivePercent > target.MaximumPercent ? "Above target" : "Within range", "")).ToList();
        return new("Your role", 80, 300, bands, [], []);
    }
}
