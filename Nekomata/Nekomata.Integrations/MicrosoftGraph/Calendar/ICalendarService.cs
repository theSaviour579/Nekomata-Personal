using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Integrations.MicrosoftGraph.Calendar;

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task<CalendarEvent> CreateFocusEventAsync(string title, DateTimeOffset start, DateTimeOffset end, string marker, CancellationToken cancellationToken = default);
    Task<CalendarEvent> MoveFocusEventAsync(string eventId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task DeleteFocusEventAsync(string eventId, CancellationToken cancellationToken = default);
}