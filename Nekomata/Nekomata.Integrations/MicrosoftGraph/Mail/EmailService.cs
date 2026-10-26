using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Mail;

public sealed class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly IMicrosoftAuthenticationService _authentication;
    private readonly string _user;

    public EmailService(HttpClient httpClient, IMicrosoftAuthenticationService authentication,MicrosoftGraphOptions? options=null)
    {
        _httpClient = httpClient;
        _authentication = authentication;
        _user=(options??new MicrosoftGraphOptions()).UserResource;
    }

    public async Task<EmailIngestionPage> GetIngestionPageAsync(string folder,DateTimeOffset since,string? cursor,int maxPages=10,CancellationToken cancellationToken=default)
    {
        if(folder is not ("inbox" or "sentitems"))throw new ArgumentOutOfRangeException(nameof(folder));
        maxPages=Math.Clamp(maxPages,1,100);
        var dateField=folder=="inbox"?"receivedDateTime":"sentDateTime";
        var filter=Uri.EscapeDataString($"{dateField} ge {since.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}");
        string? url=string.IsNullOrWhiteSpace(cursor)
            ?$"{_user}/mailFolders/{folder}/messages?$filter={filter}&$top=100&$select=id,subject,from,toRecipients,sentDateTime,receivedDateTime,importance,isRead,hasAttachments,bodyPreview,body,uniqueBody,webLink,conversationId,categories,ccRecipients&$orderby={dateField}%20asc"
            :cursor;
        var result=new List<EmailMessage>();
        for(var page=0;url is not null&&page<maxPages;page++)
        {
            using var request=await CreateRequestAsync(HttpMethod.Get,url,cancellationToken);
            request.Headers.Add("Prefer","outlook.body-content-type=\"text\"");
            using var response=await _httpClient.SendAsync(request,cancellationToken);
            await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
            var payload=await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken:cancellationToken);
            result.AddRange(payload?.Value.Select(Map)??[]);
            url=payload?.NextLink;
        }
        return new(result.DistinctBy(x=>x.Id).OrderBy(x=>x.ReceivedAt).ToList(),url,url is null);
    }

    public async Task<IReadOnlyList<EmailMessage>> GetUnreadInboxAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 100);
        var requestUri =
            $"{_user}/mailFolders/inbox/messages?$filter=isRead%20eq%20false&$top={maximum}" +
            "&$select=id,subject,from,receivedDateTime,importance,isRead,hasAttachments,bodyPreview,webLink,conversationId,categories,ccRecipients";

        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);

        return payload?.Value.Select(Map).ToList() ?? [];
    }

    public async Task<IReadOnlyList<EmailMessage>> GetRecentInboxAsync(
        int maximum = 100,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 200);
        var requestUri = $"{_user}/mailFolders/inbox/messages?$top={maximum}" +
            "&$select=id,subject,from,toRecipients,receivedDateTime,importance,isRead,hasAttachments,bodyPreview,webLink,conversationId,categories,ccRecipients" +
            "&$orderby=receivedDateTime%20desc";
        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);
        return payload?.Value.Select(Map).ToList() ?? [];
    }

    public Task<IReadOnlyList<EmailMessage>> SearchSentMessagesAsync(string query, CancellationToken cancellationToken = default) =>
        SearchMailboxAsync(query, 50, $"{_user}/mailFolders/sentitems/messages", cancellationToken);

    public async Task<string> GetMailboxAddressAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"{_user}?$select=mail,userPrincipalName", cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        using var profile = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = profile.RootElement;
        var address = root.TryGetProperty("mail", out var mail) ? mail.GetString() : null;
        if (string.IsNullOrWhiteSpace(address) && root.TryGetProperty("userPrincipalName", out var upn)) address = upn.GetString();
        return !string.IsNullOrWhiteSpace(address) ? address : throw new InvalidOperationException("The connected mailbox has no address.");
    }

    public async Task<IReadOnlyList<EmailMessage>> SearchInboxAndSentAsync(string query, CancellationToken cancellationToken = default)
    {
        var inbox = SearchMailboxAsync(query, 50, $"{_user}/mailFolders/inbox/messages", cancellationToken);
        var sent = SearchSentMessagesAsync(query, cancellationToken);
        await Task.WhenAll(inbox, sent);
        return inbox.Result.Concat(sent.Result).DistinctBy(x => x.Id).OrderByDescending(x => x.ReceivedAt).ToList();
    }

    public Task<IReadOnlyList<EmailMessage>> SearchMessagesAsync(string query, int maximum = 20, CancellationToken cancellationToken = default) =>
        SearchMailboxAsync(query, maximum, $"{_user}/messages", cancellationToken);

    private async Task<IReadOnlyList<EmailMessage>> SearchMailboxAsync(
        string query,
        int maximum,
        string resource,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        maximum = Math.Clamp(maximum, 1, 50);
        var safeQuery = query.Replace("\"", "").Trim();
        var search = Uri.EscapeDataString($"\"{safeQuery}\"");
        var requestUri =
            $"{resource}?$search={search}&$top={maximum}" +
            "&$select=id,subject,from,toRecipients,sentDateTime,receivedDateTime,importance,isRead,hasAttachments,bodyPreview,webLink,conversationId,categories,ccRecipients";
        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        request.Headers.TryAddWithoutValidation("ConsistencyLevel", "eventual");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
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
            $"{_user}/messages/{Uri.EscapeDataString(messageId)}?$select=body",
            cancellationToken);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken);
        return message?.Body?.Content?.Trim() ?? "";
    }
    public async Task<IReadOnlyList<string>> GetRecentSentMessageBodiesAsync(
        int maximum = 5,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 10);
        var requestUri =
            $"{_user}/mailFolders/sentitems/messages?$top={maximum}" +
            "&$select=body&$orderby=sentDateTime%20desc";
        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);
        return payload?.Value
            .Select(message => message.Body?.Content?.Trim())
            .Where(body => !string.IsNullOrWhiteSpace(body))
            .Select(body => body!.Length > 2000 ? body[..2000] : body)
            .ToList() ?? [];
    }
    public async Task<IReadOnlyList<EmailMessage>> GetRecentSentMessagesAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default)
    {
        maximum = Math.Clamp(maximum, 1, 100);
        var requestUri = $"{_user}/mailFolders/sentitems/messages?$top={maximum}" +
            "&$select=id,subject,from,toRecipients,sentDateTime,importance,isRead,hasAttachments,bodyPreview,webLink,conversationId,categories,ccRecipients" +
            "&$orderby=sentDateTime%20desc";
        using var request = await CreateRequestAsync(HttpMethod.Get, requestUri, cancellationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);
        return payload?.Value.Select(Map).ToList() ?? [];
    }
    public async Task<EmailMessage?> GetLatestConversationMessageAsync(string conversationId, CancellationToken cancellationToken = default)
        => (await GetConversationMessagesAsync(conversationId, cancellationToken)).OrderByDescending(x => x.ReceivedAt).FirstOrDefault();

    public async Task<IReadOnlyList<EmailMessage>> GetConversationMessagesAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var filter = Uri.EscapeDataString($"conversationId eq '{conversationId.Replace("'", "''")}' and isDraft eq false");
        string? url = $"{_user}/messages?$filter={filter}&$top=100&$select=id,subject,from,toRecipients,ccRecipients,receivedDateTime,sentDateTime,bodyPreview,uniqueBody,conversationId";
        var messages = new List<EmailMessage>();
        for (var page = 0; url != null && page < 10; page++)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, url, cancellationToken);
            request.Headers.TryAddWithoutValidation("Prefer", "outlook.body-content-type=\"text\"");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
            var payload = await response.Content.ReadFromJsonAsync<GraphMessageResponse>(cancellationToken: cancellationToken);
            messages.AddRange(payload?.Value.Select(Map) ?? []);
            url = payload?.NextLink;
        }
        if (url != null) throw new InvalidOperationException("Conversation is too large to safely identify its latest reply.");
        return messages.OrderByDescending(x => x.ReceivedAt).ToList();
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

        var resource = $"{_user}/messages/{Uri.EscapeDataString(messageId)}/createReplyAll";
        using var create = await CreateRequestAsync(HttpMethod.Post, resource, cancellationToken);
        create.Content = JsonContent.Create(new { comment = body.Trim() });
        using var createdResponse = await _httpClient.SendAsync(create, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(createdResponse,cancellationToken);
        var draft = await createdResponse.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty draft response.");
        if (string.IsNullOrWhiteSpace(draft.Id))
            throw new InvalidOperationException("Microsoft Graph did not return a draft ID.");

        return new EmailDraftResult
        {
            Id = draft.Id,
            WebLink = draft.WebLink ?? ""
        };
    }

    public async Task<EmailDraftResult> CreateNewDraftAsync(
        IReadOnlyList<string> to,
        string subject,
        string body,
        IReadOnlyList<string>? cc = null,
        CancellationToken cancellationToken = default)
    {
        if (to.Count == 0 || to.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one recipient address is required.", nameof(to));
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Draft text cannot be empty.", nameof(body));

        using var create = await CreateRequestAsync(HttpMethod.Post, $"{_user}/messages", cancellationToken);
        create.Content = JsonContent.Create(new
        {
            subject = subject.Trim(),
            body = new { contentType = "Text", content = body.Trim() },
            toRecipients = to.Select(address => new { emailAddress = new { address = address.Trim() } }).ToArray(),
            ccRecipients = (cc ?? []).Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(address => new { emailAddress = new { address = address.Trim() } }).ToArray()
        });
        using var createdResponse = await _httpClient.SendAsync(create, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(createdResponse, cancellationToken);
        var draft = await createdResponse.Content.ReadFromJsonAsync<GraphMessage>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned an empty draft response.");
        if (string.IsNullOrWhiteSpace(draft.Id))
            throw new InvalidOperationException("Microsoft Graph did not return a draft ID.");
        return new EmailDraftResult { Id = draft.Id, WebLink = draft.WebLink ?? "" };
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
            $"{_user}/messages/{Uri.EscapeDataString(draftId)}",
            cancellationToken);
        request.Content = JsonContent.Create(new
        {
            body = new { contentType = "Text", content = body.Trim() }
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
    }

    public async Task SendDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("A draft ID is required.", nameof(draftId));

        using var request = await CreateRequestAsync(
            HttpMethod.Post,
            $"{_user}/messages/{Uri.EscapeDataString(draftId)}/send",
            cancellationToken);
        request.Content = new StringContent(string.Empty);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
    }

    public async Task SendMessageAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        using var request = await CreateRequestAsync(HttpMethod.Post, $"{_user}/sendMail", cancellationToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject = subject.Trim(),
                body = new { contentType = "Text", content = body.Trim() },
                toRecipients = new[] { new { emailAddress = new { address = recipient.Trim() } } }
            },
            saveToSentItems = true
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
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
            $"{_user}/messages/{Uri.EscapeDataString(messageId)}",
            cancellationToken);
        request.Content = JsonContent.Create(new { categories });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
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

    private static EmailMessage Map(GraphMessage source)
    {
        // Senders and recipients are people the AI-use policy protects: remember them for the AI placeholders.
        Nekomata.AI.Privacy.PrivacyDirectory.AddPerson(source.From?.EmailAddress?.Name, source.From?.EmailAddress?.Address);
        foreach (var r in source.ToRecipients.Concat(source.CcRecipients))
            Nekomata.AI.Privacy.PrivacyDirectory.AddPerson(r.EmailAddress?.Name, r.EmailAddress?.Address);
        return MapMessage(source);
    }

    private static EmailMessage MapMessage(GraphMessage source) => new()
    {
        Id = source.Id ?? "",
        Subject = string.IsNullOrWhiteSpace(source.Subject) ? "(No subject)" : source.Subject,
        SenderName = source.From?.EmailAddress?.Name ?? source.From?.EmailAddress?.Address ?? "Unknown sender",
        SenderAddress = source.From?.EmailAddress?.Address ?? "",
        ReceivedAt = source.ReceivedDateTime ?? source.SentDateTime ?? DateTimeOffset.Now,
        Importance = source.Importance ?? "normal",
        IsRead = source.IsRead,
        HasAttachments = source.HasAttachments,
        BodyPreview = source.BodyPreview ?? "",
        BodyContent = source.UniqueBody?.Content ?? source.Body?.Content ?? source.BodyPreview ?? "",
        WebLink = source.WebLink ?? "",
        ConversationId = source.ConversationId ?? "",
        CcRecipients = source.CcRecipients
            .Select(recipient => recipient.EmailAddress?.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        ToRecipients = source.ToRecipients
            .Select(recipient => recipient.EmailAddress?.Name ?? recipient.EmailAddress?.Address)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        ToRecipientAddresses = source.ToRecipients
            .Select(recipient => recipient.EmailAddress?.Address)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Categories = source.Categories
    };

    private sealed class GraphMessageResponse
    {
        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
        [JsonPropertyName("value")]
        public List<GraphMessage> Value { get; init; } = [];
    }

    private sealed class GraphMessage
    {
        public string? Id { get; init; }
        public string? Subject { get; init; }
        public GraphRecipient? From { get; init; }
        public DateTimeOffset? ReceivedDateTime { get; init; }
        public DateTimeOffset? SentDateTime { get; init; }
        public string? Importance { get; init; }
        public bool IsRead { get; init; }
        public bool HasAttachments { get; init; }
        public string? BodyPreview { get; init; }
        public GraphItemBody? Body { get; init; }
        public GraphItemBody? UniqueBody { get; init; }
        public string? WebLink { get; init; }
        public string? ConversationId { get; init; }
        public List<GraphRecipient> CcRecipients { get; init; } = [];
        public List<GraphRecipient> ToRecipients { get; init; } = [];
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
