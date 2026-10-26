using Nekomata.Core.Commitments;
using Xunit;

namespace Nekomata.Tests;

public sealed class CommitmentOwnWordsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void A_promise_in_the_quoted_thread_is_not_the_users()
    {
        const string sent = "Thanks Sam, noted.\r\n\r\nFrom: Sam Jones <sam@x.co.uk>\r\nSent: 21 October 2026 16:02\r\nTo: Alex Morgan\r\nSubject: Printer\r\n\r\nI'll deal with it tomorrow.";
        var found = new CommitmentEngine().Detect([new CommitmentEvidence("Microsoft Outlook", "m1", sent, "Alex", "sam@x.co.uk", Now, true)], Now);
        Assert.Empty(found);
    }

    [Fact]
    public void The_users_own_promise_above_the_thread_still_counts()
    {
        const string sent = "I'll send you the report tomorrow.\r\n\r\n-----Original Message-----\r\nFrom: Sam\r\nCan you send the report?";
        var found = new CommitmentEngine().Detect([new CommitmentEvidence("Microsoft Outlook", "m2", sent, "Alex", "sam@x.co.uk", Now, true)], Now);
        Assert.Single(found);
        Assert.Equal(CommitmentDirection.OwedByMe, found[0].Direction);
    }

    [Theory]
    [InlineData("Will do.\nOn Tue, 21 Oct 2026 at 16:02, Sam Jones wrote:\n> I'll deal with it tomorrow", "Will do.")]
    [InlineData("Sorted.\n________________________________\nFrom: Kev\nSent: Monday\nI'll fix it", "Sorted.")]
    [InlineData("No thread here, I'll check it.", "No thread here, I'll check it.")]
    public void Own_words_stop_at_the_quoted_thread(string body, string expected) =>
        Assert.Equal(expected, CommitmentEngine.OwnWords(body).Trim());
}
