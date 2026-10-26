using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianFactRelevanceTests
{
    private static readonly string[] Facts =
    {
        "Sarah prefers calls over email",
        "Sarah's team is moving office in November",
        "Tom Baker is off on Fridays",
        "the design licence renews in March",
        "Alex's parking space is B12"
    };

    [Fact]
    public void ForMeeting_PicksFactsNamingAttendees()
    {
        var result = GuardianFactRelevance.ForMeeting(Facts, "Ops sync", new[] { "Sarah Jones", "alex.morgan@example.com" });
        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains("Sarah", f));
    }

    [Fact]
    public void ForMeeting_MatchesAddressParts() =>
        Assert.Contains("Tom Baker is off on Fridays",
            GuardianFactRelevance.ForMeeting(Facts, "Catch up", new[] { "tom.baker@example.com" }));

    [Fact]
    public void ForMeeting_MatchesDistinctiveSubjectWords() =>
        Assert.Equal(new[] { "the design licence renews in March" },
            GuardianFactRelevance.ForMeeting(Facts, "Design licence renewal review", new[] { "vendor@designco.example" }));

    [Fact]
    public void ForMeeting_IgnoresTheUserAndGenericWords() =>
        Assert.Empty(GuardianFactRelevance.ForMeeting(Facts, "Weekly meeting", new[] { "Alex Morgan" }));
}
