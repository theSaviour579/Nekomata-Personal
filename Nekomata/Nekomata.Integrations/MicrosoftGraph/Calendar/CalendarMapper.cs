using System.Globalization;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Integrations.MicrosoftGraph.Calendar;

internal static class CalendarMapper
{
    public static CalendarEvent? Map(CalendarService.GraphCalendarEvent source)
    {
        if (!DateTimeOffset.TryParse(source.Start?.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var start) ||
            !DateTimeOffset.TryParse(source.End?.DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var end))
            return null;

        return new CalendarEvent
        {
            Id = source.Id ?? string.Empty,
            Subject = string.IsNullOrWhiteSpace(source.Subject) ? "Untitled event" : source.Subject,
            Start = start,
            End = end,
            IsAllDay = source.IsAllDay,
            Location = source.Location?.DisplayName ?? string.Empty,
            Organiser = source.Organizer?.EmailAddress?.Name ?? string.Empty,
            Attendees = (source.Attendees ?? [])
                .Select(attendee => attendee.EmailAddress?.Name ?? attendee.EmailAddress?.Address ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WebLink = source.WebLink ?? string.Empty,
            BodyPreview = source.BodyPreview ?? string.Empty
        };
    }
}