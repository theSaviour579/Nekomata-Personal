using System.Net;
using System.Text;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Copilot;
using Xunit;

namespace Nekomata.Tests;

public sealed class CopilotChatServiceTests
{
    [Fact]
    public async Task Access_check_requests_every_required_delegated_scope()
    {
        var authentication = new RecordingAuthentication();
        var service = Create(authentication, _ => Json("{\"id\":\"conversation-1\"}"));

        await service.VerifyAccessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CopilotChatService.RequiredScopes.Order(), authentication.Scopes.Order());
    }

    [Fact]
    public async Task Chat_creates_conversation_and_returns_last_message_without_web_grounding()
    {
        var requests = new List<(string Path, string Body)>();
        var service = Create(new RecordingAuthentication(), request =>
        {
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            requests.Add((request.RequestUri!.AbsolutePath, body));
            return request.RequestUri.AbsolutePath.EndsWith("/conversations", StringComparison.Ordinal)
                ? Json("{\"id\":\"conversation-1\"}")
                : Json("{\"messages\":[{\"text\":\"question\"},{\"text\":\"answer\"}]}");
        });

        var answer = await service.AskAsync("Hello", false, TestContext.Current.CancellationToken);

        Assert.Equal("answer", answer);
        Assert.Collection(requests,
            request => Assert.EndsWith("/copilot/conversations", request.Path),
            request =>
            {
                Assert.EndsWith("/copilot/conversations/conversation-1/chat", request.Path);
                Assert.Contains("\"isWebEnabled\":false", request.Body);
                Assert.Contains("\"text\":\"Hello\"", request.Body);
            });
    }

    private static CopilotChatService Create(IMicrosoftAuthenticationService authentication,
        Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new Handler(responder)) { BaseAddress = new Uri("https://graph.microsoft.com/beta/") }, authentication);

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingAuthentication : IMicrosoftAuthenticationService
    {
        public IReadOnlyCollection<string> Scopes { get; private set; } = [];
        public Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TokenResult { AccessToken = "token" });
        public Task<TokenResult> GetTokenForScopesAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
        {
            Scopes = scopes;
            return Task.FromResult(new TokenResult { AccessToken = "token", AccountName = "licensed@example.com" });
        }
        public Task<string?> GetConnectedAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("licensed@example.com");
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
