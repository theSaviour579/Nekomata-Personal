using Nekomata.Core.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class InboxTriageTextTests
{
    [Fact]
    public void StripQuoted_DropsOutlookThread()
    {
        var body = "Hi Sam,\nI'll send the data tomorrow.\nThanks\nAlex\n\nFrom: Sam Wells <sam@x.com>\nSent: Monday\nSubject: data\nCan you send it?";
        Assert.Equal("Hi Sam,\nI'll send the data tomorrow.\nThanks\nAlex", InboxTriageText.StripQuoted(body));
    }

    [Fact]
    public void StripQuoted_DropsGmailStyleQuote() =>
        Assert.Equal("Will do.", InboxTriageText.StripQuoted("Will do.\r\n\r\nOn Mon, 12 Oct 2026 at 09:00, Tom <tom@x.com> wrote:\r\n> please"));

    [Fact]
    public void StripQuoted_KeepsTextWithoutQuote() =>
        Assert.Equal("Just this.", InboxTriageText.StripQuoted("  Just this. "));

    [Theory]
    [InlineData("noreply@service.com", "Service", "Your invoice")]
    [InlineData("notifications@github.com", "GitHub", "PR merged")]
    [InlineData("sam.wells@client.co.uk", "Sam Wells", "Automatic reply: data")]
    [InlineData("sam.wells@client.co.uk", "Sam Wells", "Accepted: Ops review")]
    [InlineData("events@vendor.com", "Vendor", "Join our webinar on productivity")]
    public void IsLikelyNoise_FlagsAutomatedMail(string address, string name, string subject) =>
        Assert.True(InboxTriageText.IsLikelyNoise(address, name, subject));

    [Theory]
    [InlineData("sam.wells@client.co.uk", "Sam Wells", "PP reporting data")]
    [InlineData("security@example.com", "InfoSec Team", "Action needed: access review")]
    public void IsLikelyNoise_KeepsPeople(string address, string name, string subject) =>
        Assert.False(InboxTriageText.IsLikelyNoise(address, name, subject));
}
