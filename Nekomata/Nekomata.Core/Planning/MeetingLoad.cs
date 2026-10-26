using System.Text.RegularExpressions;

namespace Nekomata.Core.Planning;

public sealed record MeetingInstance(string Subject, string Organiser, DateTimeOffset Start, DateTimeOffset End, bool IsOrganizer,
    string ResponseStatus, int Attendees, bool Cancelled);

public sealed record RecurringMeeting(string Subject, string Organiser, int Occurrences, double HoursPerMonth, int AverageMinutes,
    int Declined, int NotAnswered, int Attendees, bool YouOrganise, string Suggestion, string Why);

/// <summary>
/// Meeting load: hours in meetings for a week against the user's budget, and a review of recurring meetings over the last
/// eight weeks (the same subject and organiser three or more times) with a suggestion for each: drop the ones he keeps
/// declining or never answers, shorten long ones, make his own weekly ones fortnightly, or keep. Pure, so it's tested.
/// </summary>
public static partial class MeetingLoad
{
    public static double Hours(IEnumerable<MeetingInstance> meetings) =>
        meetings.Where(m => !m.Cancelled && m.Attendees > 0 && !m.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase))
            .Sum(m => (m.End - m.Start).TotalHours);

    public static string Normalise(string subject) =>
        SpaceRegex().Replace(NoiseRegex().Replace(subject.ToLowerInvariant(), " "), " ").Trim();

    public static IReadOnlyList<RecurringMeeting> Recurring(IEnumerable<MeetingInstance> pastEightWeeks)
    {
        var result = new List<RecurringMeeting>();
        foreach (var g in pastEightWeeks.Where(m => m.Attendees > 0 && !m.Cancelled)
                     .GroupBy(m => (Normalise(m.Subject), m.Organiser.ToLowerInvariant())).Where(g => g.Count() >= 3))
        {
            var list = g.OrderBy(m => m.Start).ToList();
            var avg = (int)Math.Round(list.Average(m => (m.End - m.Start).TotalMinutes));
            var declined = list.Count(m => m.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase));
            var none = list.Count(m => m.ResponseStatus is "none" or "notResponded" or "" && !m.IsOrganizer);
            var attended = list.Count - declined;
            var hoursPerMonth = attended * avg / 60.0 * (4.33 / 8);
            var weekly = list.Count >= 6;
            var you = list.Any(m => m.IsOrganizer);
            var attendees = (int)Math.Round(list.Average(m => m.Attendees));
            var (suggestion, why) =
                declined * 2 >= list.Count ? ("Drop it", $"you declined {declined} of {list.Count}")
                : none * 2 >= list.Count ? ("Drop it or reply", $"you didn't answer {none} of {list.Count}")
                : avg >= 60 && attendees >= 5 && !you ? ("Ask to shorten", $"{avg} minutes with {attendees} people")
                : avg >= 60 && you ? ("Shorten to 45", $"you run it at {avg} minutes")
                : you && weekly && attendees >= 3 ? ("Make it fortnightly", "weekly, and you organise it")
                : ("Keep", "");
            result.Add(new(list[^1].Subject, list[^1].Organiser, list.Count, Math.Round(hoursPerMonth, 1), avg, declined, none, attendees, you, suggestion, why));
        }
        return result.OrderByDescending(r => r.Suggestion != "Keep").ThenByDescending(r => r.HoursPerMonth).ToList();
    }

    [GeneratedRegex(@"\b(?:re|fw|fwd|updated|canceled|cancelled|invitation|weekly|monthly|week\s*\d+|\d{1,2}[/.-]\d{1,2}(?:[/.-]\d{2,4})?)\b|[^a-z0-9 ]")]
    private static partial Regex NoiseRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}

/// <summary>Daily start, finish and lunch from the time ledger, and when to say something about them.</summary>
public static class WorkPattern
{
    public sealed record Nudge(string Key, string Title, string Detail);

    public static IReadOnlyList<Nudge> Nudges(IReadOnlyList<TimeEntry> lastSevenWorkingDays, TimeSpan endOfDay, DateTime today, DateTime? lastFocus)
    {
        var nudges = new List<Nudge>();
        var week = lastSevenWorkingDays.Where(d => d.Date > today.AddDays(-7) && d.Date < today && TimeLedger.Total(d) >= 120).ToList();
        var late = week.Where(d => d.LastActive is { } l && l.TimeOfDay >= endOfDay.Add(TimeSpan.FromMinutes(90))).ToList();
        if (late.Count >= 3)
            nudges.Add(new("late", $"You've worked late {late.Count} days this week",
                $"Finished after {endOfDay.Add(TimeSpan.FromMinutes(90)):hh\\:mm} on {string.Join(", ", late.Select(d => d.Date.ToString("ddd")))}. Worth a proper finish today?"));
        var recent = lastSevenWorkingDays.Where(d => d.Date < today && TimeLedger.Total(d) >= 240).OrderByDescending(d => d.Date).Take(3).ToList();
        if (recent.Count == 3 && recent.All(d => d.LongestMiddayBreak < 20))
            nudges.Add(new("lunch", "No proper lunch break for three days", "Not even twenty minutes away from the keyboard at lunchtime since " +
                                                                               recent[^1].Date.ToString("dddd") + ". Take a proper break today."));
        var longDays = week.Where(d => d.FirstActive is { } f && d.LastActive is { } l && (l - f).TotalHours >= 10.5).ToList();
        if (longDays.Count >= 2 && late.Count < 3)
            nudges.Add(new("long", $"{longDays.Count} days over ten and a half hours this week", "Long days add up; anything that can wait until Monday?"));
        if (lastFocus is { } focus && (today - focus.Date).TotalDays >= 5 && today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            nudges.Add(new("focus", "No focus time for a week", $"Your last focus block was {focus:dddd d MMM}. Worth protecting an hour this afternoon."));
        return nudges;
    }
}
