using System.Net;
using System.Text;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Xunit;

namespace Nekomata.Tests;

public sealed class EmailServiceReplyAllTests
{
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
            },
            request =>
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                Assert.EndsWith(
                    "/me/messages/draft-1",
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