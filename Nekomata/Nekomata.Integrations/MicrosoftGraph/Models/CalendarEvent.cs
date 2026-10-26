namespace Nekomata.Integrations.MicrosoftGraph.Models;

public sealed class CalendarEvent
{
    public string Id { get; init; } = string.Empty;
    public string Subject { get; init; } = "Untitled event";
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public string Location { get; init; } = string.Empty;
    public string Organiser { get; init; } = string.Empty;
    public IReadOnlyList<string> Attendees { get; init; } = [];
    public string WebLink { get; init; } = string.Empty;
    public string BodyPreview { get; init; } = string.Empty;
    public string OnlineMeetingUrl { get; init; } = string.Empty;
    /// <summary>the user's response: none, organizer, tentativelyAccepted, accepted, declined or notResponded.</summary>
    public string ResponseStatus { get; init; } = string.Empty;
    public bool IsOrganizer { get; init; }
    public string ShowAs { get; init; } = string.Empty;
    public bool IsCancelled { get; init; }
    public IReadOnlyList<string> OptionalAttendeeAddresses { get; init; } = [];
    /// <summary>An invitation the user hasn't answered yet.</summary>
    public bool IsPendingInvite => !IsOrganizer && !IsCancelled &&
        (ResponseStatus.Equals("notResponded", StringComparison.OrdinalIgnoreCase) || ResponseStatus.Equals("none", StringComparison.OrdinalIgnoreCase));
    public bool IsNekomataManaged => BodyPreview.Contains("NEKOMATA:", StringComparison.OrdinalIgnoreCase);
    public string TimeLabel => IsAllDay ? "All day" : $"{Start:HH:mm}–{End:HH:mm}";
    public string DurationLabel => IsAllDay ? "Protected throughout the day" : $"{Math.Max(0, (int)(End - Start).TotalMinutes)} min";
}