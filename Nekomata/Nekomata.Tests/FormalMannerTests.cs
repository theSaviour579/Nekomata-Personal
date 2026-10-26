using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class FormalMannerTests
{
    [Theory]
    [InlineData("Done.", false, "Done, Sir.")]
    [InlineData("Good evening, Alex.", true, "Good evening, Sir.")]
    [InlineData("Morning Alex. Three things today.", true, "Good morning, Sir. Three things today.")]
    [InlineData("Done, Alex.", false, "Done, Sir.")]
    [InlineData("Alex, the backup failed overnight.", true, "Sir, the backup failed overnight.")]
    [InlineData("Okay. Just start talking while I'm speaking.", false, "Very good, Sir. Just start talking while I'm speaking.")]
    [InlineData("Morning. Three things today.", true, "Good morning, Sir. Three things today.")]
    [InlineData("No. Nothing critical. I'd leave things alone.", false, "No, Sir. Nothing critical. I'd leave things alone.")]
    [InlineData("Sorry, I couldn't reach your calendar.", false, "My apologies, Sir. I couldn't reach your calendar.")]
    [InlineData("I wouldn't. Alex has room. Shall I give them to Alex?", false, "I wouldn't. Alex has room. Shall I give them to Alex, Sir?")]
    [InlineData("You have an approval waiting.", true, "Sir, you have an approval waiting.")]
    [InlineData("Sam wants the PP data by Thursday.", true, "Sir, Sam wants the PP data by Thursday.")]
    [InlineData("Sam wants the PP data by Thursday.", false, "Sam wants the PP data by Thursday.")]
    [InlineData("Very good, Sir. It's done.", true, "Very good, Sir. It's done.")]
    public void Speaks_formally_without_overdoing_it(string text, bool unprompted, string expected)
    {
        var original = FormalManner.UserFirstName;
        FormalManner.UserFirstName = "Alex";
        try { Assert.Equal(expected, FormalManner.Apply(text, unprompted)); }
        finally { FormalManner.UserFirstName = original; }
    }

    [Theory]
    [InlineData("Stop calling me Sir", "off")]
    [InlineData("call me Alex", "off")]
    [InlineData("call me sir", "on")]
    public void Parses_the_switch(string text, string argument)
    {
        var m = Nekomata.Core.Guardian.Commands.GuardianCommandParser.Parse(text);
        Assert.Equal(Nekomata.Core.Guardian.Commands.GuardianCommandIntent.FormalManner, m.Intent);
        Assert.Equal(argument, m.Argument);
    }

    [Fact]
    public void Can_be_switched_off()
    {
        FormalManner.Enabled = false;
        try { Assert.Equal("Done.", FormalManner.Apply("Done.", true)); }
        finally { FormalManner.Enabled = true; }
    }
}
