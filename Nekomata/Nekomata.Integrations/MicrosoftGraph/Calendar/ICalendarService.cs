using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Integrations.MicrosoftGraph.Calendar;

public enum InviteResponse { Accept, Tentative, Decline }

/// <summary>A stretch of a colleague's calendar shown as away (out of office) or working elsewhere, from free/busy only.</summary>
public sealed record AwayBlock(string Email, DateTimeOffset Start, DateTimeOffset End, string Status);

public interface ICalendarService
{
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task<CalendarEvent> CreateFocusEventAsync(string title, DateTimeOffset start, DateTimeOffset end, string marker, CancellationToken cancellationToken = default);
    Task<CalendarEvent> MoveFocusEventAsync(string eventId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task DeleteFocusEventAsync(string eventId, CancellationToken cancellationToken = default);
    /// <summary>Accepts, tentatively accepts or declines an invitation, with a comment (and optionally a proposed new time).</summary>
    Task RespondToInviteAsync(string eventId, InviteResponse response, string comment,
        DateTimeOffset? proposedStart = null, DateTimeOffset? proposedEnd = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This calendar can't respond to invitations.");

    /// <summary>Colleagues' out-of-office blocks between two times (Graph getSchedule: free/busy only, never subjects or details).</summary>
    Task<IReadOnlyList<AwayBlock>> GetAwayAsync(IReadOnlyList<string> emails, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This calendar can't read colleagues' availability.");
}