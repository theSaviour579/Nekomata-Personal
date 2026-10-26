using Nekomata.Core.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class QuickCaptureTests
{
    [Theory]
    [InlineData("G: chase Dell about the warranty Friday", "chase Dell about the warranty Friday")]
    [InlineData("<p>guardian:  remember the comms room key is with Kev</p>", "remember the comms room key is with Kev")]
    [InlineData("g - order toner", "order toner")]
    public void Extracts(string input, string expected) => Assert.Equal(expected, QuickCapture.Extract(input));

    [Theory]
    [InlineData("Good morning")]
    [InlineData("GDPR request from Sam")]
    [InlineData("G:")]
    public void Ignores(string input) => Assert.Null(QuickCapture.Extract(input));

    [Fact]
    public void Decides_place()
    {
        Assert.Equal("office", NetworkPlace.Decide(["gw:10.0.0.1", "dns:example.local"], ["dns:example.local"], ["gw:192.168.1.1"]));
        Assert.Equal("home", NetworkPlace.Decide(["gw:192.168.1.1"], ["dns:example.local"], ["GW:192.168.1.1"]));
        Assert.Equal("", NetworkPlace.Decide(["gw:172.20.10.1"], ["dns:example.local"], ["gw:192.168.1.1"]));
    }
}
