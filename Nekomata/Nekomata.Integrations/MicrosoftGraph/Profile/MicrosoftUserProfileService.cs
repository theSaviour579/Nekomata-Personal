using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.Profile;

public sealed class MicrosoftUserProfileService(HttpClient http, IMicrosoftAuthenticationService authentication) : IMicrosoftUserProfileService
{
    public async Task<MicrosoftUserProfile?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(await authentication.GetConnectedAccountAsync(cancellationToken))) return null;
        var token = await authentication.GetTokenForScopesAsync(["User.Read"], cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, "me?$select=displayName,jobTitle");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GraphUser>(cancellationToken: cancellationToken);
        return result is null ? null : new(result.DisplayName?.Trim() ?? "", result.JobTitle?.Trim() ?? "");
    }

    private sealed class GraphUser { public string? DisplayName { get; init; } public string? JobTitle { get; init; } }
}
