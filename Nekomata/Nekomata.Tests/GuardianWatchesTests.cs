using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianWatchesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 16, 0, 0, TimeSpan.FromHours(1));

    [Theory]
    [InlineData("Tell me when Sam replies", WatchKind.Reply, "Sam", "")]
    [InlineData("let me know when Phil Jones gets back to me about the budget", WatchKind.Reply, "Phil Jones", "the budget")]
    [InlineData("let me know when Kev messages me on Teams", WatchKind.TeamsMessage, "Kev", "")]
    public void Understands_what_to_watch(string text, WatchKind kind, string target, string about)
    {
        var w = GuardianWatches.Parse(text, Now)!;
        Assert.Equal(kind, w.Kind);
        Assert.Equal(target, w.Target);
        Assert.Equal(about, w.About);
        Assert.Equal(GuardianCommandIntent.AddWatch, GuardianCommandParser.Parse(text).Intent);
    }

    [Fact]
    public void Tonight_lasts_until_ten_tomorrow_otherwise_a_week()
    {
        Assert.Equal(new DateTime(2026, 10, 22, 10, 0, 0), GuardianWatches.Parse("tell me when Sam replies tonight", Now)!.ExpiresAt.LocalDateTime);
        Assert.Equal(Now.AddDays(7), GuardianWatches.Parse("tell me when Sam replies", Now)!.ExpiresAt);
    }

    private static WatchEvidence Evidence(WatchMail[]? mail = null, WatchChat[]? chats = null) =>
        new(mail ?? [], chats ?? []);

    [Fact]
    public void Fires_only_on_new_evidence_from_the_right_source()
    {
        var reply = GuardianWatches.Parse("tell me when Sam replies about the budget", Now)!;
        Assert.Null(GuardianWatches.Check(reply, Evidence(mail: [new("Sam Jones", "sam@x.co.uk", "Re: budget", Now.AddHours(-1))])));
        Assert.Null(GuardianWatches.Check(reply, Evidence(mail: [new("Sam Jones", "sam@x.co.uk", "Lunch?", Now.AddMinutes(5))])));
        Assert.Equal("Sam has replied: \"Re: budget renewal\".",
            GuardianWatches.Check(reply, Evidence(mail: [new("Sam Jones", "sam@x.co.uk", "Re: budget renewal", Now.AddMinutes(5))])));

        var teams = GuardianWatches.Parse("let me know when Kev messages me", Now)!;
        Assert.Null(GuardianWatches.Check(teams, Evidence(chats: [new("Alex Morgan", "hi", Now.AddMinutes(1), true)])));
        Assert.StartsWith("Kev has messaged you on Teams", GuardianWatches.Check(teams, Evidence(chats: [new("Kev Hart", "report's done", Now.AddMinutes(1), false)])));
    }

    [Theory]
    [InlineData("what are you watching?", GuardianCommandIntent.ListWatches, "")]
    [InlineData("stop watching Sam", GuardianCommandIntent.CancelWatch, "Sam")]
    [InlineData("cancel all watches", GuardianCommandIntent.CancelWatch, "")]
    public void Lists_and_cancels(string text, GuardianCommandIntent intent, string argument)
    {
        var m = GuardianCommandParser.Parse(text);
        Assert.Equal(intent, m.Intent);
        Assert.Equal(argument, m.Argument);
    }
}
