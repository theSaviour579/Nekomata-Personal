using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Core.Guardian.Anticipation;

public static class MeetingPreparationPolicy
{
    public static bool IsDue(CalendarEvent meeting, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(meeting.Id) && !meeting.IsAllDay && !meeting.IsNekomataManaged &&
        meeting.Attendees.Count > 0 && meeting.End > meeting.Start && meeting.Start > now && meeting.Start <= now.AddMinutes(15);
    public static string Key(CalendarEvent meeting) => $"meeting-prep:{meeting.Id}:{meeting.Start.UtcDateTime:O}";
}
