using Nekomata.AI.Models.Meetings;
using Nekomata.Core.Meetings;
using Xunit;

namespace Nekomata.Tests;
public class MeetingNotesEvidenceTests
{
    [Fact]
    public void Unsupported_action_is_rejected() => Assert.False(MeetingNotesEvidence.Supports("Discussed search behaviour.",
        new() { Title = "Send examples", Evidence = "Alex will send examples." }));
    [Fact]
    public void Exact_note_excerpt_is_accepted() => Assert.True(MeetingNotesEvidence.Supports("Alex will send examples. Date not agreed.",
        new() { Title = "Send examples", Evidence = "Alex will send examples." }));
    [Fact]
    public void Empty_notes_cannot_create_actions() => Assert.False(MeetingNotesEvidence.Supports("", new() { Title = "Send examples", Evidence = "" }));
    [Fact]
    public void Unknown_action_kind_is_rejected() => Assert.False(MeetingNotesEvidence.Supports("Delete all records.",
        new() { Title = "Delete records", Kind = "Delete", Evidence = "Delete all records." }));
}
