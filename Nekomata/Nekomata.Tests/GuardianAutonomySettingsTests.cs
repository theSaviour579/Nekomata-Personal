using Nekomata.Core.Guardian.Autonomy;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianAutonomySettingsTests
{
    [Fact]
    public void Defaults_AreAutoAsTheUserApproved()
    {
        var settings = new GuardianAutonomySettings();
        Assert.All(Enum.GetValues<AutonomyAction>(), a => Assert.Equal(AutonomyMode.Auto, settings.Get(a)));
    }

    [Fact]
    public void Ask_IsDowngradedToOffWhereUnsupported()
    {
        var settings = new GuardianAutonomySettings();
        settings.Set(AutonomyAction.OffPlanNudges, AutonomyMode.Ask);
        settings.Set(AutonomyAction.MoveOwnBlocks, AutonomyMode.Ask);
        Assert.Equal(AutonomyMode.Off, settings.Get(AutonomyAction.OffPlanNudges));
        Assert.Equal(AutonomyMode.Ask, settings.Get(AutonomyAction.MoveOwnBlocks));
    }

    [Fact]
    public void Describe_GroupsByMode()
    {
        var settings = new GuardianAutonomySettings();
        settings.Set(AutonomyAction.PromisesToTasks, AutonomyMode.Ask);
        settings.Set(AutonomyAction.WeeklySlipping, AutonomyMode.Off);
        var text = settings.Describe();
        Assert.Contains("ask first before I turn email promises into tasks", text);
        Assert.Contains("Switched off: monday slipping summary", text);
        Assert.Contains("never move meetings", text);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var settings = new GuardianAutonomySettings();
        settings.Set(AutonomyAction.MoveOwnBlocks, AutonomyMode.Ask);
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<GuardianAutonomySettings>(json)!;
        Assert.Equal(AutonomyMode.Ask, back.Get(AutonomyAction.MoveOwnBlocks));
    }
}
