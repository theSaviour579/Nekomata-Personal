using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Context;

public sealed class GraphContextService(HttpClient httpClient, IMicrosoftAuthenticationService authentication,MicrosoftGraphOptions? options=null) : IGraphContextService
{
    private readonly string _user=(options??new MicrosoftGraphOptions()).UserResource;
    public async Task<GraphChatIngestionPage> GetChatIngestionPageAsync(DateTimeOffset since,string? cursor,int maxPages=10,CancellationToken cancellationToken=default)
    {
        maxPages=Math.Clamp(maxPages,1,100);
        string? chatUrl=string.IsNullOrWhiteSpace(cursor)?$"{_user}/chats?$top=50":cursor;
        var result=new List<GraphChatMessage>();
        var currentUserId=await GetCurrentUserIdAsync(cancellationToken);
        var attempted=0;var unreadable=0;HttpRequestException? lastUnreadable=null;
        for(var chatPage=0;chatUrl is not null&&chatPage<maxPages;chatPage++)
        {
            var chats=await GetAsync<GraphList<Chat>>(chatUrl,cancellationToken);
            foreach(var chat in chats.Value)
            {
                attempted++;
                try{result.AddRange(await ReadChatMessagesAsync(chat.Id,50,since,currentUserId,cancellationToken));}
                catch(HttpRequestException ex) when(IsUnreadableChat(ex)){unreadable++;lastUnreadable=ex;}
            }
            chatUrl=chats.NextLink;
        }
        ThrowWhenNoChatWasReadable(attempted,unreadable,lastUnreadable);
        return new(result.DistinctBy(x=>$"{x.ChatId}:{x.Id}").OrderBy(x=>x.CreatedAt).ToList(),chatUrl,chatUrl is null);
    }
    public async Task<IReadOnlyList<GraphChatMessage>> GetRecentChatMessagesAsync(
        int maximumChats = 20, int messagesPerChat = 10, int lookbackDays = 7,
        CancellationToken cancellationToken = default)
    {
        maximumChats = Math.Clamp(maximumChats, 1, 50);
        messagesPerChat = Math.Clamp(messagesPerChat, 1, 50);
        lookbackDays = Math.Clamp(lookbackDays, 1, 90);
        var oldestAllowed = DateTimeOffset.UtcNow.AddDays(-lookbackDays);
        var currentUserId = await GetCurrentUserIdAsync(cancellationToken);
        var chats = await GetAsync<GraphList<Chat>>($"{_user}/chats?$top={maximumChats}", cancellationToken);
        var result = new List<GraphChatMessage>();
        var attempted = 0; var unreadable = 0; HttpRequestException? lastUnreadable = null;
        foreach (var chat in chats.Value)
        {
            attempted++;
            try { result.AddRange(await ReadChatMessagesAsync(chat.Id, messagesPerChat, oldestAllowed, currentUserId, cancellationToken, singlePage: true)); }
            catch (HttpRequestException ex) when (IsUnreadableChat(ex)) { unreadable++; lastUnreadable = ex; }
        }
        ThrowWhenNoChatWasReadable(attempted, unreadable, lastUnreadable);
        return result.OrderByDescending(x => x.CreatedAt).ToList();
    }

    /// <summary>
    /// Messages in one chat from <paramref name="since"/> onwards. Graph rejects a <c>createdDateTime</c> filter on chat
    /// messages, so this reads newest first and stops paging once a page reaches older history.
    /// </summary>
    private async Task<List<GraphChatMessage>> ReadChatMessagesAsync(string chatId, int pageSize, DateTimeOffset since,
        string currentUserId, CancellationToken cancellationToken, bool singlePage = false)
    {
        var found = new List<GraphChatMessage>();
        string? messageUrl = $"chats/{Uri.EscapeDataString(chatId)}/messages?$top={pageSize}&$orderby=createdDateTime%20desc";
        for (var messagePage = 0; messageUrl is not null && messagePage < 100; messagePage++)
        {
            var messages = await GetAsync<GraphList<ChatMessage>>(messageUrl, cancellationToken);
            found.AddRange(messages.Value.Where(x => x.MessageType == "message" && x.CreatedDateTime >= since).Select(x => new GraphChatMessage
            {
                Id = x.Id, ChatId = chatId, Sender = x.From?.User?.DisplayName ?? "",
                Body = StripHtml(x.Body?.Content ?? ""), CreatedAt = x.CreatedDateTime,
                IsFromCurrentUser = x.From?.User?.Id == currentUserId
            }));
            messageUrl = singlePage || messages.Value.Any(x => x.CreatedDateTime < since) ? null : messages.NextLink;
        }
        if (messageUrl is not null) throw new InvalidOperationException("Teams message history exceeded the bounded 100-page safety limit.");
        return found;
    }

    /// <summary>
    /// With application permissions Graph answers 403 (InsufficientPrivileges) for some individual chats even though
    /// most are readable, so one unreadable chat must not stop the rest. See <see cref="ThrowWhenNoChatWasReadable"/>.
    /// </summary>
    private static bool IsUnreadableChat(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound;

    /// <summary>Skipping unreadable chats is only safe while some chats are readable; if none are, that is a real permission failure.</summary>
    private static void ThrowWhenNoChatWasReadable(int attempted, int unreadable, HttpRequestException? last)
    {
        if (attempted > 0 && unreadable == attempted && last is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(last).Throw();
    }

    public async Task<IReadOnlyList<GraphChatMessage>> GetChatActivityAsync(DateTimeOffset since, int maximumChats = 30, int messagesPerChat = 20,
        CancellationToken cancellationToken = default)
    {
        maximumChats = Math.Clamp(maximumChats, 1, 50);
        messagesPerChat = Math.Clamp(messagesPerChat, 1, 50);
        var currentUserId = await GetCurrentUserIdAsync(cancellationToken);
        GraphList<ChatInfo> chats;
        try
        {
            chats = await GetAsync<GraphList<ChatInfo>>(
                $"{_user}/chats?$top={maximumChats}&$expand=lastMessagePreview&$orderby=lastMessagePreview/createdDateTime%20desc", cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Some tenants refuse the ordering; read a wider page and order it here instead.
            chats = await GetAsync<GraphList<ChatInfo>>($"{_user}/chats?$top=50&$expand=lastMessagePreview", cancellationToken);
        }
        var active = chats.Value
            .Where(c => c.LastMessagePreview is null || c.LastMessagePreview.CreatedDateTime >= since)
            .OrderByDescending(c => c.LastMessagePreview?.CreatedDateTime ?? DateTimeOffset.MinValue)
            .Take(maximumChats).ToList();
        var result = new List<GraphChatMessage>();
        var attempted = 0; var unreadable = 0; HttpRequestException? lastUnreadable = null;
        foreach (var chat in active)
        {
            attempted++;
            try
            {
                var messages = await GetAsync<GraphList<ChatMessage>>(
                    $"chats/{Uri.EscapeDataString(chat.Id)}/messages?$top={messagesPerChat}&$orderby=createdDateTime%20desc", cancellationToken);
                result.AddRange(messages.Value.Where(x => x.MessageType == "message" && x.CreatedDateTime >= since).Select(x => new GraphChatMessage
                {
                    Id = x.Id, ChatId = chat.Id, Sender = x.From?.User?.DisplayName ?? "", SenderId = x.From?.User?.Id ?? "",
                    Body = StripHtml(x.Body?.Content ?? ""), CreatedAt = x.CreatedDateTime,
                    IsFromCurrentUser = x.From?.User?.Id == currentUserId,
                    ChatTopic = chat.Topic ?? "", ChatType = chat.ChatType ?? "", ChatWebUrl = chat.WebUrl ?? "",
                    Importance = string.IsNullOrWhiteSpace(x.Importance) ? "normal" : x.Importance,
                    MentionsCurrentUser = x.Mentions.Any(m => m.Mentioned?.User?.Id == currentUserId)
                }));
            }
            catch (HttpRequestException ex) when (IsUnreadableChat(ex)) { unreadable++; lastUnreadable = ex; }
        }
        ThrowWhenNoChatWasReadable(attempted, unreadable, lastUnreadable);
        return result.OrderBy(x => x.CreatedAt).ToList();
    }

    public async Task<GraphPerson?> GetManagerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = await GetAsync<Person>($"{_user}/manager?$select=id,displayName,mail", cancellationToken);
            return string.IsNullOrWhiteSpace(manager.DisplayName) ? null : new GraphPerson(manager.Id, manager.DisplayName, manager.Mail ?? "");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public async Task<IReadOnlyList<GraphTodoTask>> GetOpenTasksAsync(CancellationToken cancellationToken = default)
    {
        var lists = await GetAllAsync<TodoList>($"{_user}/todo/lists?$top=100", cancellationToken);
        var result = new List<GraphTodoTask>();
        foreach (var list in lists)
        {
            var tasks = await GetAllAsync<TodoTask>(
                $"{_user}/todo/lists/{Uri.EscapeDataString(list.Id)}/tasks?$filter=status%20ne%20'completed'&$top=100", cancellationToken);
            result.AddRange(tasks.Select(x => new GraphTodoTask
            {
                Id = x.Id, ListId = list.Id, ListName = list.DisplayName ?? "", Title = x.Title, Status = x.Status,
                DueAt = x.DueDateTime?.ToOffset(), LastModifiedAt = x.LastModifiedDateTime
            }));
        }
        return result;
    }

    // Teams' self chat has the fixed id 48:notes. Microsoft Graph doesn't list it among the user's chats and many tenants
    // refuse to read it; try once and return nothing if refused.
    public async Task<IReadOnlyList<GraphChatMessage>> GetSelfChatMessagesAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = await GetAsync<GraphList<ChatMessage>>("chats/48:notes/messages?$top=20", cancellationToken);
            return messages.Value.Where(x => x.MessageType == "message" && x.CreatedDateTime >= since).Select(x => new GraphChatMessage
            {
                Id = x.Id, ChatId = "48:notes", Sender = x.From?.User?.DisplayName ?? "", SenderId = x.From?.User?.Id ?? "",
                Body = StripHtml(x.Body?.Content ?? ""), CreatedAt = x.CreatedDateTime, IsFromCurrentUser = true, ChatType = "self"
            }).ToList();
        }
        catch (Exception) { return []; }
    }

    public async Task<GraphPresence> GetPresenceAsync(CancellationToken cancellationToken = default)
    {
        var presence = await GetAsync<Presence>($"{_user}/presence", cancellationToken);
        return new GraphPresence { Availability = presence.Availability, Activity = presence.Activity };
    }

    public async Task<IReadOnlyList<GraphPlannerTask>> GetPlannerTasksAsync(CancellationToken cancellationToken = default)
    {
        var response=await GetAllAsync<PlannerTask>($"{_user}/planner/tasks?$top=100",cancellationToken);
        return response.Where(x=>x.PercentComplete<100).Select(x=>new GraphPlannerTask
        {
            Id=x.Id,PlanId=x.PlanId,BucketId=x.BucketId,Title=x.Title,PercentComplete=x.PercentComplete,
            DueAt=x.DueDateTime,LastModifiedAt=x.LastModifiedDateTime,AssigneeIds=x.Assignments.Keys.ToList()
        }).ToList();
    }

    public async Task SendChatMessageAsync(string chatId, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatId)) throw new InvalidOperationException("The source Teams conversation is unavailable.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"chats/{Uri.EscapeDataString(chatId)}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await authentication.GetTokenAsync(cancellationToken)).AccessToken);
        request.Content = JsonContent.Create(new { body = new { contentType = "text", content } });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
    }

    /// <summary>
    /// The Teams sender id of the mailbox owner. In app-only mode a GUID in <c>MicrosoftGraph:UserId</c> is used
    /// directly, which avoids a directory read (<c>GET users/{id}</c>) that would otherwise need the extra
    /// tenant-wide <c>User.ReadBasic.All</c> application permission. Otherwise the id is read from Graph.
    /// </summary>
    private async Task<string> GetCurrentUserIdAsync(CancellationToken cancellationToken)
    {
        if (options is { UsesClientCredentials: true } && Guid.TryParse(options.UserId, out var objectId))
            return objectId.ToString();
        return (await GetAsync<User>($"{_user}?$select=id", cancellationToken)).Id;
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", (await authentication.GetTokenAsync(cancellationToken)).AccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await GraphResponseDiagnostics.EnsureSuccessAsync(response,cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Microsoft Graph returned an empty response for {uri}.");
    }

    private async Task<IReadOnlyList<T>> GetAllAsync<T>(string uri,CancellationToken cancellationToken)
    {
        string? next=uri;var result=new List<T>();
        for(var page=0;next is not null&&page<100;page++)
        {
            var value=await GetAsync<GraphList<T>>(next,cancellationToken);
            result.AddRange(value.Value);next=value.NextLink;
        }
        if(next is not null)throw new InvalidOperationException("Microsoft Graph collection exceeded the bounded 100-page safety limit.");
        return result;
    }

    private static string StripHtml(string value) => System.Net.WebUtility.HtmlDecode(
        System.Text.RegularExpressions.Regex.Replace(value, "<[^>]+>", " ")).Trim();

    private sealed class GraphList<T> { [JsonPropertyName("value")] public List<T> Value { get; init; } = []; [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; init; } }
    private sealed class Chat { public string Id { get; init; } = ""; }
    private sealed class User { public string Id { get; init; } = ""; }
    private sealed class ChatInfo
    {
        public string Id { get; init; } = ""; public string? Topic { get; init; } public string? ChatType { get; init; }
        public string? WebUrl { get; init; } public ChatPreview? LastMessagePreview { get; init; }
    }
    private sealed class ChatPreview { public DateTimeOffset CreatedDateTime { get; init; } }
    private sealed class Person { public string Id { get; init; } = ""; public string DisplayName { get; init; } = ""; public string? Mail { get; init; } }
    private sealed class ChatMessage
    {
        public string Id { get; init; } = ""; public string MessageType { get; init; } = "";
        public DateTimeOffset CreatedDateTime { get; init; } public ChatIdentitySet? From { get; init; }
        public ItemBody? Body { get; init; }
        public string? Importance { get; init; }
        public List<ChatMention> Mentions { get; init; } = [];
    }
    private sealed class ChatMention { public ChatIdentitySet? Mentioned { get; init; } }
    private sealed class ChatIdentitySet { public Identity? User { get; init; } }
    private sealed class Identity { public string Id { get; init; } = ""; public string DisplayName { get; init; } = ""; }
    private sealed class ItemBody { public string Content { get; init; } = ""; }
    private sealed class TodoList { public string Id { get; init; } = ""; public string? DisplayName { get; init; } }
    private sealed class TodoTask
    {
        public string Id { get; init; } = ""; public string Title { get; init; } = ""; public string Status { get; init; } = "";
        public GraphDateTime? DueDateTime { get; init; } public DateTimeOffset? LastModifiedDateTime { get; init; }
    }
    private sealed class PlannerTask
    {
        public string Id {get;init;}="";public string PlanId{get;init;}="";public string BucketId{get;init;}="";
        public string Title{get;init;}="";public int PercentComplete{get;init;}public DateTimeOffset? DueDateTime{get;init;}
        public DateTimeOffset? LastModifiedDateTime{get;init;}public Dictionary<string,System.Text.Json.JsonElement> Assignments{get;init;}=[];
    }
    private sealed class GraphDateTime
    {
        public string DateTime { get; init; } = ""; public string TimeZone { get; init; } = "UTC";
        public DateTimeOffset? ToOffset() => DateTimeOffset.TryParse(DateTime, out var value) ? value : null;
    }
    private sealed class Presence { public string Availability { get; init; } = "Unknown"; public string Activity { get; init; } = "Unknown"; }
}
