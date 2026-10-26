using System.Net;
using System.Text;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Xunit;

namespace Nekomata.Tests;

public sealed class GraphContextServiceAppOnlyTests
{
    private const string ObjectId = "0f8fad5b-d9cb-469f-a165-70867728950e";

    [Fact]
    public async Task App_only_object_id_avoids_the_directory_read_and_still_marks_own_messages()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new MicrosoftGraphOptions
        {
            AuthenticationMode = "ClientCredentials", ClientId = "app", TenantId = "tenant", ClientSecret = "secret", UserId = ObjectId
        });

        var page = await service.GetChatIngestionPageAsync(
            DateTimeOffset.UtcNow.AddDays(-30), null, 10, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(handler.Paths, x => x.EndsWith($"users/{ObjectId}?$select=id", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, x => x.StartsWith("chats/chat-1/messages?", StringComparison.Ordinal) && x.Contains("$orderby=createdDateTime%20desc", StringComparison.Ordinal) && !x.Contains("$filter", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Paths, x => x.Contains("asc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(handler.Paths, x => x.StartsWith($"users/{ObjectId}/chats", StringComparison.Ordinal));
        var mine = Assert.Single(page.Items, x => x.Id == "m-own");
        Assert.True(mine.IsFromCurrentUser);
        Assert.False(Assert.Single(page.Items, x => x.Id == "m-other").IsFromCurrentUser);
    }

    [Fact]
    public async Task App_only_upn_still_reads_the_user_id_from_graph()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new MicrosoftGraphOptions
        {
            AuthenticationMode = "ClientCredentials", ClientId = "app", TenantId = "tenant", ClientSecret = "secret", UserId = "david@example.com"
        });

        var page = await service.GetChatIngestionPageAsync(
            DateTimeOffset.UtcNow.AddDays(-30), null, 10, TestContext.Current.CancellationToken);

        Assert.Contains(handler.Paths, x => x.StartsWith("users/david", StringComparison.OrdinalIgnoreCase) && x.EndsWith("?$select=id", StringComparison.Ordinal));
        Assert.True(Assert.Single(page.Items, x => x.Id == "m-own").IsFromCurrentUser);
    }

    [Fact]
    public async Task Delegated_mode_reads_the_signed_in_user_from_graph()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, new MicrosoftGraphOptions { ClientId = "app" });

        var messages = await service.GetRecentChatMessagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(handler.Paths, x => x.StartsWith("me?$select=id", StringComparison.Ordinal));
        Assert.True(Assert.Single(messages, x => x.Id == "m-own").IsFromCurrentUser);
    }

    [Fact]
    public async Task Unreadable_chats_are_skipped_so_the_readable_ones_still_ingest()
    {
        var handler = new RecordingHandler(["chat-1", "chat-2", "chat-3"], forbidden: ["chat-2"]);
        var service = CreateService(handler, AppOnly());

        var page = await service.GetChatIngestionPageAsync(
            DateTimeOffset.UtcNow.AddDays(-30), null, 10, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "chat-1", "chat-3" }, page.Items.Select(x => x.ChatId).Distinct().Order().ToArray());
        Assert.Equal(4, page.Items.Count);
    }

    [Fact]
    public async Task Unreadable_chats_are_skipped_for_recent_messages_too()
    {
        var handler = new RecordingHandler(["chat-1", "chat-2"], forbidden: ["chat-1"]);
        var service = CreateService(handler, AppOnly());

        var messages = await service.GetRecentChatMessagesAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(messages, x => Assert.Equal("chat-2", x.ChatId));
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task When_every_chat_is_unreadable_the_permission_failure_is_still_reported()
    {
        var handler = new RecordingHandler(["chat-1", "chat-2"], forbidden: ["chat-1", "chat-2"]);
        var service = CreateService(handler, AppOnly());

        var failure = await Assert.ThrowsAnyAsync<HttpRequestException>(() => service.GetChatIngestionPageAsync(
            DateTimeOffset.UtcNow.AddDays(-30), null, 10, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
    }

    private static MicrosoftGraphOptions AppOnly() => new()
    {
        AuthenticationMode = "ClientCredentials", ClientId = "app", TenantId = "tenant", ClientSecret = "secret", UserId = ObjectId
    };

    private static GraphContextService CreateService(RecordingHandler handler, MicrosoftGraphOptions options) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") },
            new StubAuthentication(), options);

    private sealed class StubAuthentication : IMicrosoftAuthenticationService
    {
        public Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TokenResult { AccessToken = "token", ExpiresOn = DateTimeOffset.UtcNow.AddHours(1) });
    }

    private sealed class RecordingHandler(string[]? chatIds = null, string[]? forbidden = null) : HttpMessageHandler
    {
        private readonly List<string> paths = [];
        public IReadOnlyList<string> Paths => paths;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery.Replace("/v1.0/", "", StringComparison.Ordinal);
            paths.Add(path);
            if (path.StartsWith("chats/", StringComparison.Ordinal) && path.Contains("/messages", StringComparison.Ordinal)
                && forbidden is not null && forbidden.Contains(path.Split('/')[1]))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("""{"error":{"code":"InsufficientPrivileges","message":"denied"}}""", Encoding.UTF8, "application/json")
                });
            }
            var chatList = "{\"value\":[" + string.Join(",", (chatIds ?? ["chat-1"]).Select(x => $"{{\"id\":\"{x}\"}}")) + "]}";
            var body = path switch
            {
                _ when path.Contains("$select=id", StringComparison.Ordinal) => $$"""{"id":"{{ObjectId}}"}""",
                _ when path.Contains("/messages", StringComparison.Ordinal) => Messages,
                _ => chatList
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string Messages => $$"""
            {"value":[
              {"id":"m-own","messageType":"message","createdDateTime":"{{DateTimeOffset.UtcNow.AddHours(-2):O}}",
               "from":{"user":{"id":"{{ObjectId}}","displayName":"Alex"} },"body":{"content":"On it"} },
              {"id":"m-other","messageType":"message","createdDateTime":"{{DateTimeOffset.UtcNow.AddHours(-1):O}}",
               "from":{"user":{"id":"someone-else","displayName":"Sam"} },"body":{"content":"Thanks"} }
            ]}
            """;
    }
}
