using Nekomata.Core.Guardian.Anticipation;
using Xunit;
namespace Nekomata.Tests;
public class MeetingBriefEvidenceTests
{
    [Fact] public void Attendee_order_and_case_do_not_change_signature() => Assert.Equal(MeetingBriefEvidence.Signature("Review",["A","b"]), MeetingBriefEvidence.Signature(" review ",["B","a"]));
    [Fact] public void Different_attendees_do_not_merge_briefs() => Assert.NotEqual(MeetingBriefEvidence.Signature("Review",["A"]), MeetingBriefEvidence.Signature("Review",["B"]));
    [Fact] public void Missing_evidence_is_not_reported_as_completion() => Assert.Empty(MeetingBriefEvidence.Changes(new Dictionary<string,string>{{"ticket","Open"}},new Dictionary<string,string>()));
    [Fact] public void Unchanged_evidence_is_quiet() => Assert.Empty(MeetingBriefEvidence.Changes(new Dictionary<string,string>{{"ticket","Open"}},new Dictionary<string,string>{{"ticket","Open"}}));
    [Fact] public void New_and_updated_evidence_are_distinguished()
    {
        var changes=MeetingBriefEvidence.Changes(new Dictionary<string,string>{{"ticket","Open"}},new Dictionary<string,string>{{"ticket","Closed"},{"email","Reply"}});
        Assert.Contains("Updated: Closed",changes); Assert.Contains("New in this brief: Reply",changes);
    }
}
