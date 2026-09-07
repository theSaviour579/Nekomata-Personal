using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Xunit;

namespace Nekomata.Tests;

public class MeetingPreparationPolicyTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void Only_prepares_in_fifteen_minute_window(int minutes, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var meeting = new CalendarEvent { Id = "meeting", Start = now.AddMinutes(minutes), End = now.AddHours(1), Attendees = ["Colleague"] };
        Assert.Equal(expected, MeetingPreparationPolicy.IsDue(meeting, now));
    }
    [Fact]
    public void Focus_blocks_are_not_meetings()
    {
        var now = DateTimeOffset.UtcNow;
        var meeting = new CalendarEvent { Id = "focus", Start = now.AddMinutes(5), End = now.AddHours(1), BodyPreview = "NEKOMATA:FOCUS", Attendees = ["Colleague"] };
        Assert.False(MeetingPreparationPolicy.IsDue(meeting, now));
    }
    [Fact]
    public void Rescheduling_changes_prompt_identity()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.NotEqual(MeetingPreparationPolicy.Key(new() { Id = "meeting", Start = start }),
            MeetingPreparationPolicy.Key(new() { Id = "meeting", Start = start.AddHours(1) }));
    }
}
