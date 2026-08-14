using System.Net.Http.Headers;
using System.Text.Json;

namespace Nekomata.Services.Halo;

public class HaloAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly HaloOptions _options;

    private string? _accessToken;
    private DateTime _expiresAt;

    public HaloAuthenticationService(
        HttpClient httpClient,
        HaloOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken)
            && DateTime.UtcNow < _expiresAt)
        {
            return _accessToken;
        }

        var token =
            await RequestTokenAsync(cancellationToken);

        _accessToken =
            token.AccessToken;

        // Refresh one minute early
        _expiresAt =
            DateTime.UtcNow
                .AddSeconds(token.ExpiresInSeconds - 60);

        return _accessToken;
    }

    private async Task<HaloTokenResponse>
        RequestTokenAsync(
            CancellationToken cancellationToken)
    {
        var content =
            new FormUrlEncodedContent(
            [
                new("grant_type","client_credentials"),
                new("client_id",_options.ClientId),
                new("client_secret",_options.ClientSecret),
                new("scope","all"),
                new("username",_options.Username),
                new("password",_options.Password)
            ]);

        var response =
            await _httpClient.PostAsync(
                $"auth/token?tenant={_options.Tenant}",
                content,
                cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream =
            await response.Content
                .ReadAsStreamAsync(cancellationToken);

        return
            (await JsonSerializer.DeserializeAsync<HaloTokenResponse>(
                stream,
                cancellationToken: cancellationToken))!;
    }
}