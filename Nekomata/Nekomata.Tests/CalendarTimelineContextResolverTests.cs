using Nekomata.Integrations.MicrosoftGraph.Models;
using Xunit;

namespace Nekomata.Tests;

public sealed class CalendarTimelineContextResolverTests
{
    private static readonly DateTimeOffset Day =
        new(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Managed_block_is_active_focus_even_when_renamed()
    {
        var block = Event("Phil Report Automation", 7, 15, 8, 0, "NEKOMATA:TASK:42");

        var context = CalendarTimelineContextResolver.Resolve(
            [block], Day.AddHours(7).AddMinutes(30));

        Assert.Same(block, context.Active);
        Assert.True(context.HasActiveFocus);
        Assert.Equal(CalendarActivityKind.Focus, context.ActiveKind);
    }

    [Fact]
    public void Visible_focus_subject_survives_a_missing_or_stale_marker()
    {
        var block = Event("Focus · Phil Report Automation", 7, 15, 8, 0);

        var context = CalendarTimelineContextResolver.Resolve(
            [block], Day.AddHours(7).AddMinutes(30));

        Assert.True(context.HasActiveFocus);
    }

    [Fact]
    public void Meeting_is_not_treated_as_an_objective()
    {
        var meeting = Event("IT meeting agenda", 9, 0, 10, 0);

        var context = CalendarTimelineContextResolver.Resolve(
            [meeting], Day.AddHours(9).AddMinutes(15));

        Assert.Same(meeting, context.Active);
        Assert.False(context.HasActiveFocus);
        Assert.Equal(CalendarActivityKind.Meeting, context.ActiveKind);
    }
    [Fact]
    public void Meeting_named_managed_event_is_not_treated_as_focus()
    {
        var meeting = Event(
            "IT Meeting agenda – objectives & structure",
            9, 35, 10, 45,
            "NEKOMATA:TASK:99");

        var context = CalendarTimelineContextResolver.Resolve(
            [meeting], Day.AddHours(10));

        Assert.Equal(CalendarActivityKind.Meeting, context.ActiveKind);
        Assert.False(context.HasActiveFocus);
    }

    [Fact]
    public void Free_time_has_no_active_objective()
    {
        var later = Event("Focus · Later task", 10, 0, 11, 0);

        var context = CalendarTimelineContextResolver.Resolve(
            [later], Day.AddHours(9));

        Assert.Null(context.Active);
        Assert.False(context.HasActiveFocus);
        Assert.Equal(CalendarActivityKind.Free, context.ActiveKind);
        Assert.Same(later, context.Next);
    }

    [Fact]
    public void Next_and_then_are_chronological()
    {
        var first = Event("Focus · First", 8, 0, 8, 30);
        var second = Event("Focus · Second", 9, 0, 9, 30);
        var third = Event("Meeting", 10, 0, 10, 30);

        var context = CalendarTimelineContextResolver.Resolve(
            [third, first, second], Day.AddHours(8).AddMinutes(10));

        Assert.Same(first, context.Active);
        Assert.Same(second, context.Next);
        Assert.Same(third, context.Then);
    }

    [Fact]
    public void Objective_transitions_at_the_calendar_boundary()
    {
        var first = Event("Focus · First", 7, 15, 8, 0);
        var second = Event("Focus · Second", 8, 0, 9, 0);
        var events = new[] { first, second };

        var before = CalendarTimelineContextResolver.Resolve(
            events, Day.AddHours(7).AddMinutes(59));
        var after = CalendarTimelineContextResolver.Resolve(
            events, Day.AddHours(8));

        Assert.Same(first, before.Active);
        Assert.Same(second, before.Next);
        Assert.Same(second, after.Active);
        Assert.Null(after.Next);
        Assert.True(after.HasActiveFocus);
    }

    private static CalendarEvent Event(
        string subject,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        string body = "") => new()
    {
        Id = Guid.NewGuid().ToString(),
        Subject = subject,
        Start = Day.AddHours(startHour).AddMinutes(startMinute),
        End = Day.AddHours(endHour).AddMinutes(endMinute),
        BodyPreview = body
    };
}