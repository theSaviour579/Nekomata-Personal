using System.Net;
using System.Text;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Xunit;

namespace Nekomata.Tests;

public sealed class EmailServiceReplyAllTests
{
    [Fact]
    public async Task Combined_search_reads_both_folders_and_deduplicates()
    {
        var handler = new RecordingHandler(_ => Json("""{"value":[{"id":"same","subject":"Ticket 123"}]}"""));
        var results = await CreateService(handler).SearchInboxAndSentAsync("123", TestContext.Current.CancellationToken);
        Assert.Single(results);
        Assert.Contains(handler.Requests, x => x.Uri.AbsolutePath.Contains("/inbox/"));
        Assert.Contains(handler.Requests, x => x.Uri.AbsolutePath.Contains("/sentitems/"));
    }

    [Fact]
    public async Task Identity_comes_from_connected_profile()
    {
        var handler = new RecordingHandler(_ => Json("""{"mail":"david@example.com","userPrincipalName":"login@example.com"}"""));
        Assert.Equal("david@example.com", await CreateService(handler).GetMailboxAddressAsync(TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Latest_conversation_message_includes_later_pages()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => Json(++calls == 1
            ? """{"value":[{"id":"old","receivedDateTime":"2026-09-01T08:00:00Z"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/messages?$skiptoken=next"}"""
            : """{"value":[{"id":"latest","receivedDateTime":"2026-09-03T08:00:00Z"}]}"""));
        var latest = await CreateService(handler).GetLatestConversationMessageAsync("thread", TestContext.Current.CancellationToken);
        Assert.Equal("latest", latest?.Id);
        Assert.Equal(2, calls);
        Assert.Contains("isDraft eq false", Uri.UnescapeDataString(handler.Requests[0].Uri.Query));
    }

    [Fact]
    public async Task Chase_search_only_reads_sent_items()
    {
        var handler = new RecordingHandler(_ => Json("{\"value\":[]}"));
        var result = await CreateService(handler).SearchSentMessagesAsync("6131", TestContext.Current.CancellationToken);
        Assert.Empty(result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/me/mailFolders/sentitems/messages", request.Uri.AbsoluteUri);
        Assert.Contains("toRecipients", request.Uri.Query);
    }
    [Fact]
    public async Task Create_reply_draft_uses_reply_all_endpoint()
    {
        var handler = new RecordingHandler(request =>
        {
            var json = request.Method == HttpMethod.Post
                ? "{\"id\":\"draft-1\",\"webLink\":\"https://outlook/draft-1\"}"
                : "{\"id\":\"draft-1\",\"webLink\":\"https://outlook/draft-1\"}";
            return Json(json);
        });
        var service = CreateService(handler);

        var result = await service.CreateReplyDraftAsync("message-1", "Thanks", TestContext.Current.CancellationToken);

        Assert.Equal("draft-1", result.Id);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith(
                    "/me/messages/message-1/createReplyAll",
                    request.Uri.AbsoluteUri,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Inbox_mapping_preserves_cc_addresses()
    {
        var handler = new RecordingHandler(_ => Json(
            """
            {
              "value": [
                {
                  "id": "message-1",
                  "subject": "Project update",
                  "from": { "emailAddress": { "name": "Sender", "address": "sender@example.com" } },
                  "receivedDateTime": "2026-08-14T08:00:00Z",
                  "ccRecipients": [
                    { "emailAddress": { "name": "First", "address": "first@example.com" } },
                    { "emailAddress": { "name": "Second", "address": "second@example.com" } }
                  ]
                }
              ]
            }
            """));
        var service = CreateService(handler);

        var message = Assert.Single(await service.GetUnreadInboxAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            ["first@example.com", "second@example.com"],
            message.CcRecipients);
        Assert.Contains("ccRecipients", handler.Requests.Single().Uri.Query);
    }

    private static EmailService CreateService(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
            },
            new FakeAuthenticationService());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeAuthenticationService : IMicrosoftAuthenticationService
    {
        public Task<TokenResult> GetTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TokenResult
            {
                AccessToken = "test-token",
                ExpiresOn = DateTimeOffset.MaxValue,
                AccountName = "test@example.com"
            });
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!));
            return Task.FromResult(responder(request));
        }
    }
}
