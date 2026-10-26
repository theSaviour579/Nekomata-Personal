using Nekomata.Core.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class MeetingLoadTests
{
    private static MeetingInstance M(string subject, int weeksAgo, int minutes = 30, string response = "accepted", bool organiser = false, int attendees = 4) =>
        new(subject, organiser ? "me" : "Sam", new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo),
            new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo).AddMinutes(minutes), organiser, response, attendees, false);

    [Fact]
    public void Suggests_per_recurring_meeting()
    {
        var past = new List<MeetingInstance>();
        for (var w = 0; w < 6; w++) past.Add(M("Ops stand-up", w, response: w < 4 ? "declined" : "accepted"));
        for (var w = 0; w < 4; w++) past.Add(M("Project board - week " + w, w, minutes: 90, attendees: 8));
        for (var w = 0; w < 8; w++) past.Add(M("IT team weekly", w, organiser: true, response: "organizer"));
        past.Add(M("One-off", 1));

        var review = MeetingLoad.Recurring(past);
        Assert.Equal(3, review.Count);
        Assert.Equal("Drop it", review.Single(r => r.Subject == "Ops stand-up").Suggestion);
        Assert.Equal("Ask to shorten", review.Single(r => r.Subject.StartsWith("Project board")).Suggestion);
        Assert.Equal("Make it fortnightly", review.Single(r => r.Subject == "IT team weekly").Suggestion);
    }

    [Fact]
    public void Hours_ignore_declined_and_solo()
    {
        Assert.Equal(1.5, MeetingLoad.Hours([M("A", 0, 60), M("B", 0, 30), M("C", 0, 60, "declined"), M("Focus", 0, 120, attendees: 0)]));
    }

    [Fact]
    public void Nudges_late_finishes_and_skipped_lunch()
    {
        var today = new DateTime(2026, 10, 23);
        var days = Enumerable.Range(1, 4).Select(i =>
        {
            var d = new TimeEntry { Date = today.AddDays(-i), FirstActive = today.AddDays(-i).AddHours(8), LastActive = today.AddDays(-i).AddHours(18.5), LongestMiddayBreak = 5 };
            d.Minutes[TimeCategory.Email] = 500;
            return d;
        }).ToList();
        var nudges = WorkPattern.Nudges(days, new TimeSpan(16, 30, 0), today, today.AddDays(-1));
        Assert.Contains(nudges, n => n.Key == "late");
        Assert.Contains(nudges, n => n.Key == "lunch");
        Assert.DoesNotContain(nudges, n => n.Key == "focus");
    }
}
