namespace Nekomata.Integrations.MicrosoftGraph.Context;

public sealed class GraphChatMessage
{
    public string Id { get; init; } = "";
    public string ChatId { get; init; } = "";
    public string Sender { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsFromCurrentUser { get; init; }
    public string SenderId { get; init; } = "";
    /// <summary>The chat's own name for group chats; empty for one-to-one chats and unnamed groups.</summary>
    public string ChatTopic { get; init; } = "";
    /// <summary>oneOnOne, group or meeting.</summary>
    public string ChatType { get; init; } = "";
    public string ChatWebUrl { get; init; } = "";
    /// <summary>normal, high or urgent, as the sender set it.</summary>
    public string Importance { get; init; } = "normal";
    public bool MentionsCurrentUser { get; init; }
}

public sealed record GraphPerson(string Id, string DisplayName, string Mail);

public sealed class GraphTodoTask
{
    public string Id { get; init; } = "";
    public string ListId { get; init; } = "";
    public string ListName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTimeOffset? DueAt { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
}

public sealed class GraphPlannerTask
{
    public string Id { get; init; } = "";
    public string PlanId { get; init; } = "";
    public string BucketId { get; init; } = "";
    public string Title { get; init; } = "";
    public int PercentComplete { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    public DateTimeOffset? LastModifiedAt { get; init; }
    public IReadOnlyList<string> AssigneeIds { get; init; } = [];
}

public sealed class GraphPresence
{
    public string Availability { get; init; } = "Unknown";
    public string Activity { get; init; } = "Unknown";
}

public interface IGraphContextService
{
    Task<GraphChatIngestionPage> GetChatIngestionPageAsync(DateTimeOffset since,string? cursor,int maxPages=10,CancellationToken cancellationToken=default) =>
        Task.FromResult(new GraphChatIngestionPage([],null,true));
    Task<IReadOnlyList<GraphChatMessage>> GetRecentChatMessagesAsync(
        int maximumChats = 20, int messagesPerChat = 10, int lookbackDays = 7,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphTodoTask>> GetOpenTasksAsync(CancellationToken cancellationToken = default);
    /// <summary>Recent messages in Teams' "chat with yourself", where the tenant exposes it; empty where it doesn't.</summary>
    Task<IReadOnlyList<GraphChatMessage>> GetSelfChatMessagesAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GraphChatMessage>>([]);
    Task<IReadOnlyList<GraphPlannerTask>> GetPlannerTasksAsync(CancellationToken cancellationToken = default);
    Task<GraphPresence> GetPresenceAsync(CancellationToken cancellationToken = default);
    Task SendChatMessageAsync(string chatId, string content, CancellationToken cancellationToken = default);
    /// <summary>Messages since <paramref name="since"/> in the chats with recent activity, with each chat's name, type and link.</summary>
    Task<IReadOnlyList<GraphChatMessage>> GetChatActivityAsync(DateTimeOffset since, int maximumChats = 30, int messagesPerChat = 20,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GraphChatMessage>>([]);
    /// <summary>The signed-in user's line manager from the directory, or null when none is set.</summary>
    Task<GraphPerson?> GetManagerAsync(CancellationToken cancellationToken = default) => Task.FromResult<GraphPerson?>(null);
}

public sealed record GraphChatIngestionPage(IReadOnlyList<GraphChatMessage> Items,string? NextCursor,bool FullyCaughtUp);
