using System.Net.Http.Headers;
using System.Text.Json;
using Nekomata.Models.Common;

namespace Nekomata.Services.Halo;

public sealed class RealHaloClient
    : IHaloClient
{
    private readonly HttpClient _httpClient;

    private readonly HaloAuthenticationService
        _authenticationService;

    private readonly HaloOptions
        _options;

    private readonly Dictionary<int, string>
        _priorityNames;

    private static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                PropertyNameCaseInsensitive = true
            };
    private static bool IsClosedStatus(
    HaloApiTicket ticket)
    {
        var status =
            ticket.StatusName?.Trim();

        if (string.IsNullOrWhiteSpace(status))
            return false;

        return status.Equals(
                   "Closed",
                   StringComparison.OrdinalIgnoreCase)
               ||
               status.Equals(
                   "Resolved",
                   StringComparison.OrdinalIgnoreCase)
               ||
               status.Equals(
                   "Completed",
                   StringComparison.OrdinalIgnoreCase)
               ||
               status.Equals(
                   "Cancelled",
                   StringComparison.OrdinalIgnoreCase);
    }
    public RealHaloClient(
        HttpClient httpClient,
        HaloAuthenticationService authenticationService,
        HaloOptions options)
    {
        _httpClient =
            httpClient;

        _authenticationService =
            authenticationService;

        _options =
            options;

        _priorityNames = new Dictionary<int, string>(
            options.PriorityMappings);
    }

    public async Task<IReadOnlyList<HaloTicket>>
        GetMyTicketsAsync(
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await _authenticationService
                .GetAccessTokenAsync(
                    cancellationToken);

        var allTickets =
            new List<HaloTicket>();

        var pageNumber = 1;

        while (true)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var page =
                await GetTicketPageAsync(
                    accessToken,
                    pageNumber,
                    cancellationToken);

            if (page.Tickets.Count == 0)
                break;

            await ResolvePriorityNamesAsync(
                accessToken,
                page.Tickets,
                cancellationToken);

            allTickets.AddRange(
                page.Tickets.Select(
                    ticket => MapTicket(
                        ticket,
                        _priorityNames.GetValueOrDefault(ticket.PriorityId))));

            System.Diagnostics.Debug.WriteLine(
                $"Halo page {pageNumber}: " +
                $"{page.Tickets.Count} ticket(s).");

            if (page.Tickets.Count <
                _options.PageSize)
            {
                break;
            }

            pageNumber++;
        }

        System.Diagnostics.Debug.WriteLine(
            $"Halo total: {allTickets.Count} ticket(s).");

        return allTickets;
    }

    private async Task<HaloTicketPage>
        GetTicketPageAsync(
            string accessToken,
            int pageNumber,
            CancellationToken cancellationToken)
    {
        var requestUrl =
            "api/Tickets" +
            "?pageinate=true" +
            $"&page_size={_options.PageSize}" +
            $"&page_no={pageNumber}" +
            "&order=last_update" +
            "&orderdesc=true";

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                requestUrl);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody =
                await response.Content
                    .ReadAsStringAsync(
                        cancellationToken);

            throw new HttpRequestException(
                $"Halo ticket request failed with " +
                $"{(int)response.StatusCode} " +
                $"{response.ReasonPhrase}. " +
                $"Response: {errorBody}");
        }

        await using var responseStream =
            await response.Content
                .ReadAsStreamAsync(
                    cancellationToken);

        var page =
            await JsonSerializer
                .DeserializeAsync<HaloTicketPage>(
                    responseStream,
                    JsonOptions,
                    cancellationToken);

        return page ??
               new HaloTicketPage();
    }

    private async Task ResolvePriorityNamesAsync(
        string accessToken,
        IEnumerable<HaloApiTicket> tickets,
        CancellationToken cancellationToken)
    {
        var unresolvedIds = tickets
            .Select(ticket => ticket.PriorityId)
            .Where(id => id > 0 && !_priorityNames.ContainsKey(id))
            .Distinct()
            .ToList();

        foreach (var priorityId in unresolvedIds)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/Priority/{priorityId}");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                accessToken);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Halo priority {priorityId} lookup returned {(int)response.StatusCode}.");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var name = FindPriorityName(document.RootElement);
                if (!string.IsNullOrWhiteSpace(name))
                    _priorityNames[priorityId] = name;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Halo priority {priorityId} lookup failed: {exception.Message}");
            }
        }
    }

    private static string? FindPriorityName(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nestedName = FindPriorityName(item);
                if (!string.IsNullOrWhiteSpace(nestedName))
                    return nestedName;
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var candidate in new[] { "name", "priority", "priority_name", "description", "value" })
        {
            if (element.TryGetProperty(candidate, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.GetString()))
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static HaloTicket MapTicket(
        HaloApiTicket ticket,
        string? resolvedPriorityName)
    {
        var priorityName = FirstNonEmpty(
            ticket.SlaPriority,
            ticket.SlaPriorityName,
            ticket.CompactSlaPriority,
            ticket.CompactSlaPriorityName,
            resolvedPriorityName,
            ticket.PriorityDisplayName,
            ticket.PriorityName);
        var hasCriticalSla = HasCriticalSla(ticket);
        var isP1 = IsP1Priority(priorityName, ticket.PriorityId) || hasCriticalSla;
        var mappedPriority = isP1
            ? TaskPriorities.Critical
            : MapPriority(priorityName, ticket.PriorityId);
        var isPhishAlert = IsPhishPriority(priorityName, ticket.PriorityId);
        var due = FirstValidDate(ticket.SlaActionDate, ticket.FixByDate, ticket.RespondByDate);

        System.Diagnostics.Debug.WriteLine(
            $"Halo #{ticket.Id}: priority id={ticket.PriorityId}, name='{priorityName}', SLA='{ticket.SlaName}', critical SLA={hasCriticalSla}, P1={isP1}, phish={isPhishAlert}");

        return new HaloTicket
        {
            Id =
                ticket.Id,

            Summary =
                ticket.Summary,

            Customer =
                ticket.ClientName,

            Status =
                ticket.StatusName ??
                ticket.StatusId.ToString(),

            Priority = mappedPriority,

            Created =
                NormaliseDate(ticket.DateOccurred)
                ?? DateTime.Now,

            Due = due,

            LastUpdatedAt = NormaliseDate(ticket.LastUpdate),

            CustomerImpact =
                ticket.IsVip,

            SecurityRelated =
                IsSecurityRelated(ticket),

            BusinessValue =
                0,

            EstimatedMinutes =
                EstimateMinutes(ticket),

                StatusId =
    ticket.StatusId,

            IsClosed = IsClosedStatus(ticket) || ticket.StatusId == 9,

            TicketTypeId =
    ticket.TicketTypeId,

            AgentName = ticket.AgentName,

            HaloPriorityName = priorityName ?? $"Priority {ticket.PriorityId}",

            IsPhishAlert = isPhishAlert,

            IsP1 = isP1,

            RequiresImmediateAttention = isP1 ||
                (mappedPriority == TaskPriorities.High &&
                 due is not null && due <= DateTime.Now.AddHours(1)),
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool HasCriticalSla(HaloApiTicket ticket)
    {
        if (ticket.SlaName?.Contains("critical", StringComparison.OrdinalIgnoreCase) == true ||
            IsP1Priority(ticket.SlaPriority, 0) ||
            IsP1Priority(ticket.SlaPriorityName, 0) ||
            IsP1Priority(ticket.CompactSlaPriority, 0) ||
            IsP1Priority(ticket.CompactSlaPriorityName, 0))
        {
            return true;
        }

        return ticket.AdditionalFields is not null &&
            ticket.AdditionalFields.Any(field =>
                field.Key.Contains("sla", StringComparison.OrdinalIgnoreCase) &&
                ContainsCriticalSlaValue(field.Value));
    }

    private static bool ContainsCriticalSlaValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return IsP1Priority(element.GetString(), 0);

        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property => ContainsCriticalSlaValue(property.Value));

        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(ContainsCriticalSlaValue);

        return false;
    }

    private static bool IsP1Priority(string? priorityName, int priorityId)
    {
        if (!string.IsNullOrWhiteSpace(priorityName))
        {
            var name = priorityName.Trim();
            return name.Contains("critical", StringComparison.OrdinalIgnoreCase)
                || name.Equals("P1", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("P1 ", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("P1-", StringComparison.OrdinalIgnoreCase);
        }

        // A missing label is not enough evidence to raise a P1 alert.
        return false;
    }

    private static bool IsPhishPriority(string? priorityName, int priorityId)
    {
        if (!string.IsNullOrWhiteSpace(priorityName))
            return priorityName.Contains("phish", StringComparison.OrdinalIgnoreCase);

        return priorityId == 5;
    }
    private static string MapPriority(
        string? priorityName,
        int priorityId)
    {
        if (!string.IsNullOrWhiteSpace(
                priorityName))
        {
            if (priorityName.Contains(
                    "critical",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriorities.Critical;
            }

            if (priorityName.Contains(
                    "p1",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriorities.Critical;
            }

            if (priorityName.Contains(
                    "high",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriorities.High;
            }

            if (priorityName.Contains(
                    "low",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriorities.Low;
            }

            if (priorityName.Contains("medium", StringComparison.OrdinalIgnoreCase) ||
                priorityName.Contains("normal", StringComparison.OrdinalIgnoreCase))
            {
                return TaskPriorities.Normal;
            }
        }

        /*
         * Halo priority IDs are tenant-specific.
         * Until a configuration map is added,
         * unknown IDs remain Normal.
         */
        return TaskPriorities.Normal;
    }

private static int EstimateMinutes(
        HaloApiTicket ticket)
    {
        if (ticket.TimeTaken > 0)
        {
            return Math.Clamp(
                (int)Math.Ceiling(
                    ticket.TimeTaken),
                15,
                240);
        }

        return ticket.PriorityName?
            .Contains(
                "critical",
                StringComparison.OrdinalIgnoreCase)
            == true
                ? 60
                : 30;
    }

    private static bool IsSecurityRelated(
        HaloApiTicket ticket)
    {
        var searchableText =
            string.Join(
                " ",
                ticket.Summary,
                ticket.Category1,
                ticket.Category2);

        return searchableText.Contains(
                   "security",
                   StringComparison.OrdinalIgnoreCase)
               ||
               searchableText.Contains(
                   "phishing",
                   StringComparison.OrdinalIgnoreCase)
               ||
               searchableText.Contains(
                   "malware",
                   StringComparison.OrdinalIgnoreCase)
               ||
               searchableText.Contains(
                   "breach",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime? FirstValidDate(
        params DateTime?[] values)
    {
        return values
            .Select(NormaliseDate)
            .FirstOrDefault(
                value => value is not null);
    }

    private static DateTime? NormaliseDate(
        DateTime? value)
    {
        if (value is null)
            return null;

        if (value.Value.Year <= 1900)
            return null;

        return value;
    }
}