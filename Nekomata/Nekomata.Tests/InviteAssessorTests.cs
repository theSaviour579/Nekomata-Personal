using Nekomata.Core.Planning;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Xunit;

namespace Nekomata.Tests;

public sealed class InviteAssessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.FromHours(1)); // Monday
    private static readonly TimeSpan DayStart = TimeSpan.FromHours(8.5), DayEnd = TimeSpan.FromHours(17);

    private static CalendarEvent Event(string id, int startHour, int minutes, string agenda = "Agenda: review the Q4 rollout plan and owners",
        bool organiser = false, string response = "accepted", string[]? optional = null, bool focus = false) => new()
    {
        Id = id, Subject = id, Start = new DateTimeOffset(Now.Date.AddHours(startHour), Now.Offset),
        End = new DateTimeOffset(Now.Date.AddHours(startHour).AddMinutes(minutes), Now.Offset),
        Attendees = ["Phil Jones <phil@x.com>", "Alex Morgan <alex@x.com>"], Organiser = "Phil Jones <phil@x.com>",
        BodyPreview = focus ? "NEKOMATA:block" : agenda, IsOrganizer = organiser, ResponseStatus = response,
        OptionalAttendeeAddresses = optional ?? []
    };

    [Fact]
    public void Clash_proposes_the_nearest_free_slot()
    {
        var invite = Event("Vendor call", 10, 60, response: "notResponded");
        var calendar = new[] { invite, Event("Ops review", 10, 60) };
        var a = InviteAssessor.Assess(invite, calendar, "alex@x.com", DayStart, DayEnd, Now);
        Assert.Equal(InviteAdvice.ProposeNewTime, a.Advice);
        Assert.Equal(InviteResponse.Tentative, a.Response);
        Assert.NotNull(a.ProposedStart);
        Assert.True(a.ProposedStart >= calendar[1].End.AddMinutes(5) || a.ProposedEnd <= calendar[1].Start.AddMinutes(-5));
        Assert.Contains("Thanks Phil", a.Comment);
    }

    [Fact]
    public void Optional_without_agenda_is_declined_and_no_agenda_asks_for_one()
    {
        var optional = Event("Catch-up", 14, 30, agenda: "", response: "notResponded", optional: ["alex@x.com"]);
        Assert.Equal(InviteAdvice.DeclineOptional, InviteAssessor.Assess(optional, [optional], "Alex@x.com", DayStart, DayEnd, Now).Advice);
        var bare = Event("Sync", 14, 30, agenda: "", response: "notResponded");
        Assert.Equal(InviteAdvice.AskForAgenda, InviteAssessor.Assess(bare, [bare], "alex@x.com", DayStart, DayEnd, Now).Advice);
    }

    [Fact]
    public void Focus_block_overlap_alone_still_accepts()
    {
        var invite = Event("Planning", 11, 60, response: "notResponded");
        var a = InviteAssessor.Assess(invite, [invite, Event("Focus", 11, 90, focus: true)], "alex@x.com", DayStart, DayEnd, Now);
        Assert.Equal(InviteAdvice.Accept, a.Advice);
        Assert.Contains(a.Reasons, r => r.Contains("focus block"));
    }
}
