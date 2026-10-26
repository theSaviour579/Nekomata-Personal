using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Core.Planning;

public enum InviteAdvice { Accept, AskForAgenda, ProposeNewTime, DeclineOptional, Tentative }

public sealed record InviteAssessment(
    CalendarEvent Invite, InviteAdvice Advice, IReadOnlyList<string> Reasons, string Comment,
    DateTimeOffset? ProposedStart, DateTimeOffset? ProposedEnd)
{
    public InviteResponse Response => Advice switch
    {
        InviteAdvice.Accept => InviteResponse.Accept,
        InviteAdvice.DeclineOptional => InviteResponse.Decline,
        _ => InviteResponse.Tentative
    };

    public string AdviceLabel => Advice switch
    {
        InviteAdvice.Accept => "accept",
        InviteAdvice.AskForAgenda => "accept tentatively and ask for an agenda",
        InviteAdvice.ProposeNewTime => ProposedStart is { } s ? $"propose {s.LocalDateTime:ddd d MMM HH:mm} instead" : "propose another time",
        InviteAdvice.DeclineOptional => "decline (you're optional)",
        _ => "accept tentatively"
    };
}

/// <summary>
/// Calendar defence: what an unanswered invitation costs the user and what to reply. It never moves anyone's meeting -
/// at most it proposes another time for the organiser to accept. Rules, strongest first:
///   clash with a meeting he's in, or outside his working day -> propose the nearest free slot of the same length
///   (tentative if there's none); optional for him with no agenda -> decline politely; no agenda -> tentative and ask
///   for one; otherwise accept. A clash with one of Guardian's own focus blocks is noted but not a reason to refuse:
///   Day Autopilot moves those.
/// </summary>
public static class InviteAssessor
{
    public static InviteAssessment Assess(CalendarEvent invite, IReadOnlyList<CalendarEvent> calendar, string myAddress,
        TimeSpan dayStart, TimeSpan dayEnd, DateTimeOffset now)
    {
        var reasons = new List<string>();
        var length = invite.End - invite.Start;
        var meetings = calendar.Where(e => e.Id != invite.Id && !e.IsAllDay && !e.IsCancelled && !e.IsNekomataManaged &&
                                           !e.ShowAs.Equals("free", StringComparison.OrdinalIgnoreCase) &&
                                           !e.ResponseStatus.Equals("declined", StringComparison.OrdinalIgnoreCase) &&
                                           (e.Attendees.Count > 0 || e.IsOrganizer)).ToList();
        var clashes = meetings.Where(e => e.Start < invite.End && e.End > invite.Start).ToList();
        var focus = calendar.Where(e => e.IsNekomataManaged && e.Start < invite.End && e.End > invite.Start).ToList();
        var local = invite.Start.LocalDateTime;
        var outside = !invite.IsAllDay && (local.TimeOfDay < dayStart || invite.End.LocalDateTime.TimeOfDay > dayEnd ||
                                           local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        var optional = myAddress.Length > 0 && invite.OptionalAttendeeAddresses.Contains(myAddress.ToLowerInvariant());
        var noAgenda = invite.BodyPreview.Trim().Length < 25 && !invite.IsAllDay;

        foreach (var c in clashes) reasons.Add($"Clashes with {c.Subject} ({c.Start.LocalDateTime:HH:mm}-{c.End.LocalDateTime:HH:mm}).");
        if (outside) reasons.Add("It's outside your working day.");
        if (optional) reasons.Add("You're an optional attendee.");
        if (noAgenda) reasons.Add("There's no agenda.");
        if (focus.Count > 0) reasons.Add("It lands on a focus block; I'll move the block if you accept.");

        var who = FirstName(invite.Organiser);
        if (clashes.Count > 0 || outside)
        {
            var slot = FindSlot(invite, meetings, length, dayStart, dayEnd, now);
            if (slot is { } s)
                return new(invite, InviteAdvice.ProposeNewTime, reasons,
                    $"{Thanks(who)} - I've a clash at that time. Would {s.LocalDateTime:dddd d MMM 'at' HH:mm} work instead?", s, s + length);
            return new(invite, InviteAdvice.Tentative, reasons,
                $"{Thanks(who)} - I've a clash at that time, so I'll try to make it but may need to drop off.", null, null);
        }
        if (optional && noAgenda)
            return new(invite, InviteAdvice.DeclineOptional, reasons,
                $"{Thanks(who)} - as I'm optional I'll sit this one out. Happy to pick up any IT actions afterwards.", null, null);
        if (noAgenda)
            return new(invite, InviteAdvice.AskForAgenda, reasons,
                $"{Thanks(who)} - could you share a quick agenda so I can come prepared?", null, null);
        if (reasons.Count == 0) reasons.Add("No clash, and it has an agenda.");
        return new(invite, InviteAdvice.Accept, reasons, "", null, null);
    }

    /// <summary>The first free slot of the same length in working hours, from the invite's day, within five working days.</summary>
    public static DateTimeOffset? FindSlot(CalendarEvent invite, IReadOnlyList<CalendarEvent> busy, TimeSpan length,
        TimeSpan dayStart, TimeSpan dayEnd, DateTimeOffset now)
    {
        var day = invite.Start.LocalDateTime.Date;
        for (int checkedDays = 0, offset = 0; checkedDays < 5 && offset < 14; offset++)
        {
            var date = day.AddDays(offset);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            checkedDays++;
            for (var t = date + dayStart; t + length <= date + dayEnd; t = t.AddMinutes(15))
            {
                var start = new DateTimeOffset(t);
                if (start < now.AddMinutes(30)) continue;
                var end = start + length;
                if (Math.Abs((start - invite.Start).TotalMinutes) < 1) continue;
                if (!busy.Any(b => b.Start < end.AddMinutes(5) && b.End > start.AddMinutes(-5))) return start;
            }
        }
        return null;
    }

    private static string Thanks(string who) => who.Length > 0 ? $"Thanks {who}" : "Thanks";

    private static string FirstName(string organiser)
    {
        var name = organiser.Split('<')[0].Trim();
        return name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    }
}
