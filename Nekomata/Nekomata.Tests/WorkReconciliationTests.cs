using Nekomata.Core.Guardian.Outcomes;
using Xunit;
namespace Nekomata.Tests;
public class WorkReconciliationTests
{
    [Theory]
    [InlineData("Resolved", true)]
    [InlineData(" closed ", true)]
    [InlineData("Completed", true)]
    [InlineData("Cancelled", false)]
    [InlineData("9", false)]
    [InlineData(null, false)]
    [InlineData("Awaiting reply", false)]
    public void Requires_explicit_completion_status(string? status, bool expected) => Assert.Equal(expected, WorkReconciliation.IsCompletedStatus(status));
    [Theory]
    [InlineData("old", "new", "me", "me", 1, true)]
    [InlineData("", "new", "me", "me", 1, false)]
    [InlineData("same", "same", "me", "me", 1, false)]
    [InlineData("old", "new", "them", "me", 1, false)]
    [InlineData("old", "new", "me", "me", -1, false)]
    public void Only_new_outgoing_evidence_after_reminder_counts(string previous, string current, string sender, string own, int minutes, bool expected)
    {
        var created = DateTimeOffset.UtcNow;
        Assert.Equal(expected, WorkReconciliation.IsNewSentEvidence(previous,current,sender,own,created.AddMinutes(minutes),created));
    }
}
