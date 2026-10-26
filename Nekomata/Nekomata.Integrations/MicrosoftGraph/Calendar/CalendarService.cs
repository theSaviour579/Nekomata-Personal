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
    private readonly string _user;

    public CalendarService(HttpClient httpClient, IMicrosoftAuthenticationService authentication,MicrosoftGraphOptions? options=null)
    {
        _httpClient = httpClient;
        _authentication = authentication;
        _user=(options??new MicrosoftGraphOptions()).UserResource;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var requestUri =
            $"{_user}/calendarView?startDateTime={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" +
            $"&endDateTime={Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))}" +
            "&$top=100&$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview,onlineMeeting,responseStatus,isOrganizer,showAs,isCancelled&$orderby=start/dateTime";

        var values = new List<GraphCalendarEvent>();
        string? pageUri = requestUri;
        for (var page = 0; pageUri is not null && page < 10; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GraphCalendarResponse>(cancellationToken: cancellationToken);
            values.AddRange(payload?.Value ?? []);
            pageUri = payload?.NextLink;
        }

        if (pageUri is not null)
            throw new InvalidOperationException("The calendar result exceeded the safe 1,000-event retrieval limit.");

        return values
            .Select(CalendarMapper.Map)
            .Where(item => item is not null)
            .Cast<CalendarEvent>()
            .OrderBy(item => item.Start)
            .ToList() ?? [];
    }


    public async Task<IReadOnlyList<AwayBlock>> GetAwayAsync(IReadOnlyList<string> emails, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
    {
        if (emails.Count == 0) return [];
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var result = new List<AwayBlock>();
        foreach (var batch in emails.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(20))
        {
            var body = new
            {
                schedules = batch,
                startTime = new { dateTime = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
                endTime = new { dateTime = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
                availabilityViewInterval = 60
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_user}/calendar/getSchedule") { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await GraphResponseDiagnostics.EnsureSuccessAsync(response, cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GraphScheduleResponse>(cancellationToken: cancellationToken);
            foreach (var schedule in payload?.Value ?? [])
                foreach (var item in schedule.ScheduleItems ?? [])
                {
                    var status = item.Status ?? "";
                    if (!status.Equals("oof", StringComparison.OrdinalIgnoreCase) && !status.Equals("workingElsewhere", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!DateTime.TryParse(item.Start?.DateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var s) ||
                        !DateTime.TryParse(item.End?.DateTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var e)) continue;
                    result.Add(new(schedule.ScheduleId ?? "", new DateTimeOffset(s, TimeSpan.Zero), new DateTimeOffset(e, TimeSpan.Zero), status));
                }
        }
        return result;
    }

    private sealed class GraphScheduleResponse { public List<GraphSchedule> Value { get; init; } = []; }
    private sealed class GraphSchedule { public string? ScheduleId { get; init; } public List<GraphScheduleItem>? ScheduleItems { get; init; } }
    private sealed class GraphScheduleItem { public string? Status { get; init; } public GraphScheduleTime? Start { get; init; } public GraphScheduleTime? End { get; init; } }
    private sealed class GraphScheduleTime { public string? DateTime { get; init; } public string? TimeZone { get; init; } }

    public async Task<CalendarEvent> CreateFocusEventAsync(
        string title,
        DateTimeOffset start,
        DateTimeOffset end,
        string marker,
        CancellationToken cancellationToken = default)
    {
        var token = await _authentication.GetTokenAsync(cancellationToken);
        // A task can legitimately be split into several focus blocks. Graph's
        // transactionId is an idempotency key for one event, not for the task as
        // a whole, so include the exact interval to keep retries stable while
        // allowing distinct segments of the same task.
        var transactionKey = $"{marker}|{start.UtcDateTime:O}|{end.UtcDateTime:O}";
        var transactionBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(transactionKey));
        var transactionId = new Guid(transactionBytes[..16]).ToString();
        var payload = new
        {
            subject = title.StartsWith("Focus · ", StringComparison.OrdinalIgnoreCase)
                ? title
                : $"Focus · {title}",
            body = new { contentType = "text", content = $"Scheduled by Nekomata.\n{marker}" },
            start = new { dateTime = start.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZoneInfo.Local.Id },
            end = new { dateTime = end.DateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = TimeZoneInfo.Local.Id },
            showAs = "busy",
            categories = new[] { "Nekomata" },
            transactionId
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_user}/events")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
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
        var resource = $"{_user}/events/{Uri.EscapeDataString(eventId)}";
        using var inspect = new HttpRequestMessage(HttpMethod.Get, resource + "?$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview");
        inspect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        inspect.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var inspected = await _httpClient.SendAsync(inspect, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(inspected,cancellationToken);
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
        await GraphResponseDiagnostics.EnsureSuccessAsync(updated,cancellationToken);
        var result = await updated.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        return CalendarMapper.Map(result)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
    }
    public async Task RespondToInviteAsync(string eventId, InviteResponse response, string comment,
        DateTimeOffset? proposedStart = null, DateTimeOffset? proposedEnd = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("A calendar event ID is required.", nameof(eventId));
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var action = response switch { InviteResponse.Accept => "accept", InviteResponse.Decline => "decline", _ => "tentativelyAccept" };
        object payload = proposedStart is { } ps && proposedEnd is { } pe && response != InviteResponse.Accept
            ? new
            {
                comment, sendResponse = true,
                proposedNewTime = new
                {
                    start = new { dateTime = ps.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" },
                    end = new { dateTime = pe.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" }
                }
            }
            : new { comment, sendResponse = true };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_user}/events/{Uri.EscapeDataString(eventId)}/{action}")
        { Content = JsonContent.Create(payload, payload.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var result = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(result, cancellationToken);
    }

    public async Task DeleteFocusEventAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("A calendar event ID is required.", nameof(eventId));

        var token = await _authentication.GetTokenAsync(cancellationToken);
        var resource = $"{_user}/events/{Uri.EscapeDataString(eventId)}";
        using var inspect = new HttpRequestMessage(HttpMethod.Get, resource + "?$select=id,subject,start,end,isAllDay,location,organizer,attendees,webLink,bodyPreview");
        inspect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        inspect.Headers.Add("Prefer", $"outlook.timezone=\"{TimeZoneInfo.Local.Id}\"");
        using var inspected = await _httpClient.SendAsync(inspect, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(inspected,cancellationToken);
        var current = await inspected.Content.ReadFromJsonAsync<GraphCalendarEvent>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty event response.");
        var mapped = CalendarMapper.Map(current)
            ?? throw new InvalidOperationException("Microsoft Graph returned an event with invalid dates.");
        if (!mapped.IsNekomataManaged)
            throw new InvalidOperationException($"'{mapped.Subject}' is protected and cannot be deleted by Guardian undo.");

        using var delete = new HttpRequestMessage(HttpMethod.Delete, resource);
        delete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await _httpClient.SendAsync(delete, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
    }
    internal sealed class GraphCalendarResponse
    {
        [JsonPropertyName("value")]
        public List<GraphCalendarEvent> Value { get; init; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
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
        public GraphOnlineMeeting? OnlineMeeting { get; init; }
        public GraphResponseStatus? ResponseStatus { get; init; }
        public bool? IsOrganizer { get; init; }
        public string? ShowAs { get; init; }
        public bool IsCancelled { get; init; }
    }
    internal sealed class GraphResponseStatus { public string? Response { get; init; } }

    internal sealed class GraphOnlineMeeting { public string? JoinUrl { get; init; } }

    internal sealed class GraphDateTime { public string? DateTime { get; init; } }
    internal sealed class GraphLocation { public string? DisplayName { get; init; } }
    internal sealed class GraphOrganiser { public GraphEmailAddress? EmailAddress { get; init; } }
    internal sealed class GraphAttendee { public GraphEmailAddress? EmailAddress { get; init; } public string? Type { get; init; } }
    internal sealed class GraphEmailAddress { public string? Name { get; init; } public string? Address { get; init; } }
}
