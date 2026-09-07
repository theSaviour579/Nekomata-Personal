using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Copilot;

public sealed class CopilotChatService(HttpClient http, IMicrosoftAuthenticationService authentication)
{
    public const string ProviderName = "Microsoft 365 Copilot (Preview)";
    public static readonly string[] RequiredScopes =
    [
        "Sites.Read.All", "Mail.Read", "People.Read.All", "OnlineMeetingTranscript.Read.All",
        "Chat.Read", "ChannelMessage.Read.All", "ExternalItem.Read.All"
    ];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _conversationId;
    private string? _conversationAccount;

    public async Task<string> AskAsync(string prompt, bool webSearchEnabled, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("A message is required.", nameof(prompt));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = await authentication.GetTokenForScopesAsync(RequiredScopes, cancellationToken);
            if (!string.Equals(_conversationAccount, token.AccountName, StringComparison.OrdinalIgnoreCase))
            {
                _conversationId = null;
                _conversationAccount = token.AccountName;
            }
            _conversationId ??= await CreateConversationAsync(token.AccessToken, cancellationToken);
            var response = await SendAsync(_conversationId, token.AccessToken, prompt, webSearchEnabled, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                _conversationId = await CreateConversationAsync(token.AccessToken, cancellationToken);
                response = await SendAsync(_conversationId, token.AccessToken, prompt, webSearchEnabled, cancellationToken);
            }
            using (response)
            {
                await EnsureAvailableAsync(response, cancellationToken);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var messages = document.RootElement.TryGetProperty("messages", out var value) && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().ToList() : [];
                var answer = messages.Count > 0 && messages[^1].TryGetProperty("text", out var text) ? text.GetString() : null;
                return string.IsNullOrWhiteSpace(answer)
                    ? throw new InvalidOperationException("Microsoft 365 Copilot returned an empty response.")
                    : answer;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task VerifyAccessAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = await authentication.GetTokenForScopesAsync(RequiredScopes, cancellationToken);
            _conversationAccount = token.AccountName;
            _conversationId = await CreateConversationAsync(token.AccessToken, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<string> CreateConversationAsync(string token, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, "copilot/conversations", token);
        request.Content = JsonContent.Create(new { });
        using var response = await http.SendAsync(request, ct);
        await EnsureAvailableAsync(response, ct);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()!
            : throw new InvalidOperationException("Microsoft 365 Copilot did not return a conversation ID.");
    }

    private async Task<HttpResponseMessage> SendAsync(string conversationId, string token, string prompt, bool web, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, $"copilot/conversations/{Uri.EscapeDataString(conversationId)}/chat", token);
        request.Content = JsonContent.Create(new
        {
            message = new { text = prompt },
            locationHint = new { timeZone = LocalTimeZone() },
            contextualResources = new { webContext = new { isWebEnabled = web } }
        });
        return await http.SendAsync(request, ct);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureAvailableAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsStringAsync(ct); // Drain without displaying potentially sensitive service detail.
        throw response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new InvalidOperationException("Copilot access was refused. Confirm the signed-in work account has a Microsoft 365 Copilot licence and an administrator has consented to the Copilot permissions."),
            HttpStatusCode.Unauthorized => new InvalidOperationException("Copilot sign-in has expired or the selected account is not eligible. Reconnect the Microsoft account and try again."),
            _ => new InvalidOperationException($"Microsoft 365 Copilot returned HTTP {(int)response.StatusCode}. The preview service may be unavailable or may have changed.")
        };
    }

    private static string LocalTimeZone() => TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana)
        ? iana : TimeZoneInfo.Local.Id;
}
