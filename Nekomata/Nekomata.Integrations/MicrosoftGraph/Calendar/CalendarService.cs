using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.Integrations.MicrosoftGraph.Calendar;

public sealed class CalendarService : ICalendarService
{
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftAuthenticationService _authentication;

    public CalendarService(HttpClient httpClient, IMicrosoftAuthenticationService authentication)
    {
        _httpClient = httpClient;
        _authentication = authentication;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var requestUri =
            $"me/calendarView?startDateTime={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" +
            $"&endDateTime={Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))}" +
            "&$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview&$orderby=start/dateTime";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GraphCalendarResponse>(cancellationToken: cancellationToken);

        return payload?.Value
            .Select(CalendarMapper.Map)
            .Where(item => item is not null)
            .Cast<CalendarEvent>()
            .OrderBy(item => item.Start)
            .ToList() ?? [];
    }


    public async Task<CalendarEvent> CreateFocusEventAsync(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string marker,
        CancellationToken cancellationToken = default)
    {
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var transactionBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(marker));
        var transactionId = new Guid(transactionBytes[..16]).ToString();
        var payload = new
        {
            subject = title.StartsWith("Focus Â· ", StringComparison.OrdinalIgnoreCase)
                ? title
                : $"Focus Â· {title}",
            body = new { contentType = "text", content = $"Scheduled by Nekomata.\n{marker}" },
            start = new { dateTime = start.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZoneInfo.Local.Id },
            end = new { dateTime = end.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZoneInfo.Local.Id },
            showAs = "busy",
            categories = new[] { "Nekomata" },
            transactionId
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "me/events")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        return CalendarMapper.Map(created)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
    }
    public async Task<CalendarEvent> MoveFocusEventAsync(
        string eventId,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("A calendar event ID is required.", nameof(eventId));

        var token = await _authentication.GetTokenAsync(cancellationToken);
        var resource = $"me/events/{Uri.EscapeDataString(eventId)}";
        using var inspect = new HttpRequestMessage(HttpMethod.Get, resource + "?$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview");
        inspect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        inspect.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var inspected = await _httpClient.SendAsync(inspect, cancellationToken);
        inspected.EnsureSuccessStatusCode();
        var current = await inspected.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        var mapped = CalendarMapper.Map(current)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
        if (!mapped.IsNekomataManaged)
            throw new InvalidOperationException($"'{mapped.Subject}' is a protected meeting. Guardian may only move Nekomata focus blocks.");

        // Graph accepts Windows time-zone names, but an AI-proposed offset can disagree
        // with the local zone around daylight-saving boundaries. UTC makes the move
        // payload unambiguous while the Prefer header still controls the response.
        var payload = new
        {
            start = new { dateTime = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
            end = new { dateTime = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" }
        };
        using var update = new HttpRequestMessage(HttpMethod.Patch, resource) { Content = JsonContent.Create(payload) };
        update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        update.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var updated = await _httpClient.SendAsync(update, cancellationToken);
        await EnsureGraphSuccessAsync(updated, "move the calendar block", cancellationToken);
        var result = await updated.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        return CalendarMapper.Map(result)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
    }
    public async Task DeleteFocusEventAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("A calendar event ID is required.", nameof(eventId));

        var token = await _authentication.GetTokenAsync(cancellationToken);
        var resource = $"me/events/{Uri.EscapeDataString(eventId)}";
        using var inspect = new HttpRequestMessage(HttpMethod.Get, resource + "?$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview");
        inspect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        inspect.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var inspected = await _httpClient.SendAsync(inspect, cancellationToken);
        inspected.EnsureSuccessStatusCode();
        var current = await inspected.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        var mapped = CalendarMapper.Map(current)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
        if (!mapped.IsNekomataManaged)
            throw new InvalidOperationException($"'{mapped.Subject}' is protected and cannot be deleted by Guardian undo.");

        using var delete = new HttpRequestMessage(HttpMethod.Delete, resource);
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await _httpClient.SendAsync(delete, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    private static async Task EnsureGraphSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (detail.Length > 600)
            detail = detail[..600] + "…";

        throw new HttpRequestException(
            $"Microsoft Graph could not {operation} ({(int)response.StatusCode} {response.ReasonPhrase}). " +
            (string.IsNullOrWhiteSpace(detail) ? "No error detail was returned." : detail),
            null,
            response.StatusCode);
    }
    internal sealed class GraphCalendarResponse
    {
        [JsonPropertyName("value")]
        public List<GraphCalendarEvent> Value { get; init; } = [];
    }

    internal sealed class GraphCalendarEvent
    {
        public string? Id { get; init; }
        public string? Subject { get; init; }
        public GraphDateTime? Start { get; init; }
        public GraphDateTime? End { get; init; }
        public bool IsAllDay { get; init; }
        public GraphLocation? Location { get; init; }
        public GraphOrganiser? Organizer { get; init; }
        public List<GraphAttendee> Attendees { get; init; } = [];
        public string? WebLink { get; init; }
        public string? BodyPreview { get; init; }
    }

    internal sealed class GraphDateTime { public string? DateTime { get; init; } }
    internal sealed class GraphLocation { public string? DisplayName { get; init; } }
    internal sealed class GraphOrganiser { public GraphEmailAddress? EmailAddress { get; init; } }
    internal sealed class GraphAttendee { public GraphEmailAddress? EmailAddress { get; init; } }
    internal sealed class GraphEmailAddress { public string? Name { get; init; } public string? Address { get; init; } }
}
