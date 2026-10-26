using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;
using Xunit;

namespace Nekomata.Tests;

public sealed class ProtocolsTests
{
    [Theory]
    [InlineData("End of day protocol", GuardianCommandIntent.RunProtocol, "End of day", "")]
    [InlineData("run the morning protocol", GuardianCommandIntent.RunProtocol, "morning", "")]
    [InlineData("incident protocol: email is down", GuardianCommandIntent.RunProtocol, "incident", "email is down")]
    [InlineData("create protocol lunch: plan my day; anything I need to worry about", GuardianCommandIntent.CreateProtocol, "lunch", "plan my day; anything I need to worry about")]
    [InlineData("delete the lunch protocol", GuardianCommandIntent.DeleteProtocol, "lunch", "")]
    [InlineData("what protocols do you have?", GuardianCommandIntent.ListProtocols, "", "")]
    [InlineData("systems check", GuardianCommandIntent.SelfCheck, "", "")]
    [InlineData("cancel the incident protocol", GuardianCommandIntent.RunProtocol, "cancel the incident", "")]
    public void Parses(string text, GuardianCommandIntent intent, string argument, string detail)
    {
        var m = GuardianCommandParser.Parse(text);
        Assert.Equal(intent, m.Intent);
        Assert.Equal(argument, m.Argument);
        Assert.Equal(detail, m.Detail);
    }

    [Fact]
    public void Finds_by_name_or_alias_and_custom_first()
    {
        var custom = new[] { new Protocol("lunch", "", ["plan my day"], [], BuiltIn: false) };
        Assert.Equal("end of day", Protocols.Find("Shutdown", custom)!.Name);
        Assert.Equal("morning", Protocols.Find("the morning", custom)!.Name);
        Assert.Equal("lunch", Protocols.Find("Lunch", custom)!.Name);
        Assert.Null(Protocols.Find("party", custom));
    }

    [Fact]
    public void Every_built_in_step_is_a_command_guardian_knows()
    {
        foreach (var p in Protocols.BuiltIn)
            foreach (var step in Protocols.StepsFor(p, "email is down")!)
            {
                var intent = GuardianCommandParser.Parse(step).Intent;
                Assert.True(intent is not GuardianCommandIntent.Unknown and not GuardianCommandIntent.ConfirmPending and not GuardianCommandIntent.RunProtocol,
                    $"{p.Name}: \"{step}\" parsed as {intent}");
            }
    }

    [Fact]
    public void Incident_needs_a_detail()
    {
        var incident = Protocols.Find("incident", [])!;
        Assert.Null(Protocols.StepsFor(incident, ""));
        Assert.Equal("declare a major incident email is down", Protocols.StepsFor(incident, "email is down")![0]);
        Assert.Equal(["plan my day", "anything I need to worry about"], Protocols.ParseSteps("plan my day; anything I need to worry about"));
    }
}
