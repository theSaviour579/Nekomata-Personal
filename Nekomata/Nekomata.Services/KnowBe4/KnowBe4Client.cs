using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nekomata.Services.KnowBe4;

public sealed class KnowBe4Client
{
    private readonly HttpClient _httpClient;
    private readonly KnowBe4Options _options;
    private IReadOnlyList<KnowBe4Failure> _cachedFailures = [];
    private DateTimeOffset _cacheExpiresAt;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public KnowBe4Client(HttpClient httpClient, KnowBe4Options options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<IReadOnlyList<KnowBe4Failure>> GetRecentFailuresAsync(CancellationToken cancellationToken = default)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiresAt) return _cachedFailures;
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt) return _cachedFailures;

            using var testsRequest = CreateRequest("v1/phishing/security_tests?per_page=500&cursor=0");
            using var testsResponse = await _httpClient.SendAsync(testsRequest, cancellationToken);
            await EnsureSuccessAsync(testsResponse, "load phishing security tests", cancellationToken);
            var tests = await ReadResultListAsync<SecurityTest>(testsResponse, cancellationToken);
            var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(_options.LookbackHours, 1, 168));
            var campaignCutoff = cutoff.AddDays(-90);
            var failures = new List<KnowBe4Failure>();

            var eligibleTests = tests
                .Where(HasFailures)
                .Where(test =>
                    !test.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ||
                    test.StartedAt is DateTimeOffset startedAt && startedAt >= campaignCutoff);

            foreach (var test in eligibleTests)
            {
                using var recipientsRequest = CreateRequest($"v1/phishing/security_tests/{test.PstId}/recipients?per_page=500&cursor=0");
                using var recipientsResponse = await _httpClient.SendAsync(recipientsRequest, cancellationToken);
                await EnsureSuccessAsync(recipientsResponse, $"load recipients for simulation {test.PstId}", cancellationToken);
                var recipients = await ReadResultListAsync<RecipientResult>(recipientsResponse, cancellationToken);

                foreach (var recipient in recipients)
                {
                    var events = FailureEvents(recipient)
                        .Where(item => item.At >= cutoff)
                        .OrderBy(item => item.At)
                        .ToList();
                    if (events.Count == 0) continue;
                    var latest = events[^1];
                    failures.Add(new KnowBe4Failure(
                        test.PstId,
                        recipient.RecipientId,
                        test.Name,
                        recipient.User?.DisplayName ?? recipient.User?.Email ?? "Unknown user",
                        recipient.User?.Email ?? string.Empty,
                        string.Join(", ", events.Select(item => item.Type).Distinct()),
                        latest.At));
                }
            }

            _cachedFailures = failures.OrderByDescending(item => item.OccurredAt).ToList();
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            return _cachedFailures;
        }
        finally
        {
            _refreshGate.Release();
        }
    }
    private static async Task<List<T>> ReadResultListAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return root.Deserialize<List<T>>() ?? [];
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            return data.Deserialize<List<T>>() ?? [];
        }
        throw new JsonException("KnowBe4 returned an unsupported response structure.");
    }
    private HttpRequestMessage CreateRequest(string relativeUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool HasFailures(SecurityTest test) =>
        test.ClickedCount > 0 || test.RepliedCount > 0 || test.AttachmentOpenCount > 0 ||
        test.MacroEnabledCount > 0 || test.DataEnteredCount > 0 || test.QrCodeScannedCount > 0;

    private static IEnumerable<(string Type, DateTimeOffset At)> FailureEvents(RecipientResult recipient)
    {
        if (recipient.ClickedAt is { } clicked) yield return ("link clicked", clicked);
        if (recipient.RepliedAt is { } replied) yield return ("replied", replied);
        if (recipient.AttachmentOpenedAt is { } attachment) yield return ("attachment opened", attachment);
        if (recipient.MacroEnabledAt is { } macro) yield return ("macro enabled", macro);
        if (recipient.DataEnteredAt is { } data) yield return ("data entered", data);
        if (recipient.QrCodeScannedAt is { } qr) yield return ("QR code scanned", qr);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        if (detail.Length > 400) detail = detail[..400] + "…";
        throw new HttpRequestException($"KnowBe4 could not {operation} ({(int)response.StatusCode} {response.ReasonPhrase}). {detail}");
    }

    private sealed class SecurityTest
    {
        [JsonPropertyName("pst_id")] public long PstId { get; init; }
        [JsonPropertyName("name")] public string Name { get; init; } = "Phishing simulation";
        [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
        [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; init; }
        [JsonPropertyName("clicked_count")] public int ClickedCount { get; init; }
        [JsonPropertyName("replied_count")] public int RepliedCount { get; init; }
        [JsonPropertyName("attachment_open_count")] public int AttachmentOpenCount { get; init; }
        [JsonPropertyName("macro_enabled_count")] public int MacroEnabledCount { get; init; }
        [JsonPropertyName("data_entered_count")] public int DataEnteredCount { get; init; }
        [JsonPropertyName("qr_code_scanned_count")] public int QrCodeScannedCount { get; init; }
    }

    private sealed class RecipientResult
    {
        [JsonPropertyName("recipient_id")] public long RecipientId { get; init; }
        [JsonPropertyName("user")] public RecipientUser? User { get; init; }
        [JsonPropertyName("clicked_at")] public DateTimeOffset? ClickedAt { get; init; }
        [JsonPropertyName("replied_at")] public DateTimeOffset? RepliedAt { get; init; }
        [JsonPropertyName("attachment_opened_at")] public DateTimeOffset? AttachmentOpenedAt { get; init; }
        [JsonPropertyName("macro_enabled_at")] public DateTimeOffset? MacroEnabledAt { get; init; }
        [JsonPropertyName("data_entered_at")] public DateTimeOffset? DataEnteredAt { get; init; }
        [JsonPropertyName("qr_code_scanned")] public DateTimeOffset? QrCodeScannedAt { get; init; }
    }

    private sealed class RecipientUser
    {
        [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
        [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
        [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
        public string DisplayName => $"{FirstName} {LastName}".Trim();
    }
}

public sealed record KnowBe4Failure(long PstId, long RecipientId, string TestName, string UserName, string Email, string FailureTypes, DateTimeOffset OccurredAt)
{
    public string EventId => $"{PstId}:{RecipientId}:{OccurredAt.UtcTicks}";
}