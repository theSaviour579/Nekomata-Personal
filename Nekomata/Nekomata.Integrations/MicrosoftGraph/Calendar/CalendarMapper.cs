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
            Organiser = Format(source.Organizer?.EmailAddress),
            Attendees = (source.Attendees ?? [])
                .Select(attendee => Format(attendee.EmailAddress))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            WebLink = source.WebLink ?? string.Empty,
            BodyPreview = source.BodyPreview ?? string.Empty,
            OnlineMeetingUrl = source.OnlineMeeting?.JoinUrl ?? string.Empty,
            ResponseStatus = source.ResponseStatus?.Response ?? string.Empty,
            IsOrganizer = source.IsOrganizer ?? false,
            ShowAs = source.ShowAs ?? string.Empty,
            IsCancelled = source.IsCancelled,
            OptionalAttendeeAddresses = (source.Attendees ?? [])
                .Where(a => string.Equals(a.Type, "optional", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a.EmailAddress?.Address))
                .Select(a => a.EmailAddress!.Address!.ToLowerInvariant()).ToList()
        };
    }

    private static string Format(CalendarService.GraphEmailAddress? value)
    {
        if (value is null) return string.Empty;
        if (string.IsNullOrWhiteSpace(value.Name)) return value.Address ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value.Address)) return value.Name;
        return $"{value.Name} <{value.Address}>";
    }
}
