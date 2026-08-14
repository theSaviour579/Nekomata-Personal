using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Mail;

public sealed class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftAuthenticationService _authentication;

    public EmailService(HttpClient httpClient, IMicrosoftAuthenticationService authentication)
    {
        _httpClient = httpClient;
        _authentication = authentication;
    }

    public async Task<IReadOnlyList<EmailMessage>> GetUnreadInboxAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 100);
        var requestUri =
            $"me/mailFolders/inbox/messages?$filter=isRead%20eq%20false&$top={maximum}" +
            "&$select=id,subject,from,receivedDateTime,importance,isRead,hasAttachments,bodyPreview,webLink,conversationId,categories,ccRecipients";

        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);

        return payload?.Value.Select(Map).ToList() ?? [];
    }

    public async Task<string> GetMessageContentAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("A message ID is required.", nameof(messageId));

        using var request = await CreateRequestAsync(
            HttpMethod.Get,
            $"me/messages/{Uri.EscapeDataString(messageId)}?$select=body",
            cancellationToken);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var message = await response.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken);
        return message?.Body?.Content?.Trim() ?? "";
    }
    public async Task<IReadOnlyList<string>> GetRecentSentMessageBodiesAsync(
        int maximum = 5,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 10);
        var requestUri =
            $"me/mailFolders/sentitems/messages?$top={maximum}" +
            "&$select=body&$orderby=sentDateTime%20desc";
        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);
        return payload?.Value
            .Select(message => message.Body?.Content?.Trim())
            .Where(body => !string.IsNullOrWhiteSpace(body))
            .Select(body => body!.Length > 2000 ? body[..2000] : body)
            .ToList() ?? [];
    }
    public async Task<EmailDraftResult> CreateReplyDraftAsync(
        string messageId,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("A message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Draft text cannot be empty.", nameof(body));

        var resource = $"me/messages/{Uri.EscapeDataString(messageId)}/createReplyAll";
        using var create = await CreateRequestAsync(HttpMethod.Post, resource, cancellationToken);
        create.Content = JsonContent.Create(new { });
        using var createdResponse = await _httpClient.SendAsync(create, cancellationToken);
        createdResponse.EnsureSuccessStatusCode();
        var draft = await createdResponse.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty draft response.");
        if (string.IsNullOrWhiteSpace(draft.Id))
            throw new InvalidOperationException("Microsoft Graph did not return a draft ID.");

        using var update = await CreateRequestAsync(
            HttpMethod.Patch,
            $"me/messages/{Uri.EscapeDataString(draft.Id)}",
            cancellationToken);
        update.Content = JsonContent.Create(new
        {
            body = new { contentType = "Text", content = body.Trim() }
        });
        using var updatedResponse = await _httpClient.SendAsync(update, cancellationToken);
        updatedResponse.EnsureSuccessStatusCode();
        var updated = await updatedResponse.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken);

        return new EmailDraftResult
        {
            Id = draft.Id,
            WebLink = updated?.WebLink ?? draft.WebLink ?? ""
        };
    }

    public async Task UpdateDraftAsync(
        string draftId,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("A draft ID is required.", nameof(draftId));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Draft text cannot be empty.", nameof(body));

        using var request = await CreateRequestAsync(
            HttpMethod.Patch,
            $"me/messages/{Uri.EscapeDataString(draftId)}",
            cancellationToken);
        request.Content = JsonContent.Create(new
        {
            body = new { contentType = "Text", content = body.Trim() }
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("A draft ID is required.", nameof(draftId));

        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"me/messages/{Uri.EscapeDataString(draftId)}/send",
            cancellationToken);
        request.Content = new StringContent(string.Empty);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
    public async Task ApplyCategoryAsync(
        string messageId,
        IReadOnlyCollection<string> existingCategories,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("A message ID is required.", nameof(messageId));
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A category is required.", nameof(category));

        var categories = existingCategories
            .Append(category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        using var request = await CreateRequestAsync(
            HttpMethod.Patch,
            $"me/messages/{Uri.EscapeDataString(messageId)}",
            cancellationToken);
        request.Content = JsonContent.Create(new { categories });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string uri,
        CancellationToken cancellationToken)
    {
        var token = await _authentication.GetTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return request;
    }

    private static EmailMessage Map(GraphMessage source) => new()
    {
        Id = source.Id ?? "",
        Subject = string.IsNullOrWhiteSpace(source.Subject) ? "(No subject)" : source.Subject,
        SenderName = source.From?.EmailAddress?.Name ?? source.From?.EmailAddress?.Address ?? "Unknown sender",
        SenderAddress = source.From?.EmailAddress?.Address ?? "",
        ReceivedAt = source.ReceivedDateTime,
        Importance = source.Importance ?? "normal",
        IsRead = source.IsRead,
        HasAttachments = source.HasAttachments,
        BodyPreview = source.BodyPreview ?? "",
        BodyContent = source.Body?.Content ?? "",
        WebLink = source.WebLink ?? "",
        ConversationId = source.ConversationId ?? "",
        CcRecipients = source.CcRecipients
            .Select(recipient => recipient.EmailAddress?.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Categories = source.Categories
    };

    private sealed class GraphMessageResponse
    {
        [JsonPropertyName("value")]
        public List<GraphMessage> Value { get; init; } = [];
    }

    private sealed class GraphMessage
    {
        public string? Id { get; init; }
        public string? Subject { get; init; }
        public GraphRecipient? From { get; init; }
        public DateTimeOffset ReceivedDateTime { get; init; }
        public string? Importance { get; init; }
        public bool IsRead { get; init; }
        public bool HasAttachments { get; init; }
        public string? BodyPreview { get; init; }
        public GraphItemBody? Body { get; init; }
        public string? WebLink { get; init; }
        public string? ConversationId { get; init; }
        public List<GraphRecipient> CcRecipients { get; init; } = [];
        public List<string> Categories { get; init; } = [];
    }


    private sealed class GraphItemBody
    {
        public string? ContentType { get; init; }
        public string? Content { get; init; }
    }
    private sealed class GraphRecipient
    {
        public GraphEmailAddress? EmailAddress { get; init; }
    }

    private sealed class GraphEmailAddress
    {
        public string? Name { get; init; }
        public string? Address { get; init; }
    }
}