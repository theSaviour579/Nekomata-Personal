using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;
using Xunit;

namespace Nekomata.Tests;

public sealed class MusicCommandTests
{
    [Theory]
    [InlineData("play music", "play", "")]
    [InlineData("Pause the music", "pause", "")]
    [InlineData("next song", "next", "")]
    [InlineData("what's playing?", "now", "")]
    [InlineData("volume to 30", "volume", "30")]
    [InlineData("turn it down", "volume", "-15")]
    [InlineData("play something to focus to", "focus", "")]
    [InlineData("play Coldplay", "search", "Coldplay")]
    [InlineData("play the deep focus playlist on Spotify", "search", "the deep focus playlist")]
    public void Parses(string text, string action, string detail)
    {
        var m = GuardianCommandParser.Parse(text);
        Assert.Equal(GuardianCommandIntent.Music, m.Intent);
        Assert.Equal(action, m.Argument);
        Assert.Equal(detail, m.Detail);
    }

    [Theory]
    [InlineData("put on hold the move to cloud project")]
    [InlineData("who is this?")]
    public void Leaves_other_commands_alone(string text) =>
        Assert.NotEqual(GuardianCommandIntent.Music, GuardianCommandParser.Parse(text).Intent);

    [Fact]
    public void The_focus_protocol_is_valid() =>
        Assert.Equal(GuardianCommandIntent.Music, GuardianCommandParser.Parse(Protocols.Find("focus", [])!.Steps[0]).Intent);
}
