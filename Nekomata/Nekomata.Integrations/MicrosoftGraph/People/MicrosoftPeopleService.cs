using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.Integrations.MicrosoftGraph.People;

public sealed class MicrosoftPeopleService(HttpClient http, IMicrosoftAuthenticationService authentication) : IMicrosoftPeopleService
{
    public async Task<IReadOnlyList<MicrosoftPerson>> GetRelevantPeopleAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(await authentication.GetConnectedAccountAsync(cancellationToken)))
            throw new InvalidOperationException("Connect Microsoft 365 before syncing people.");
        var token = await authentication.GetTokenForScopesAsync(["People.Read"], cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "me/people?$top=100&$select=id,displayName,jobTitle,scoredEmailAddresses,personType");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PeoplePage>(cancellationToken: cancellationToken) ?? new();
        return page.Value
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .Where(x => x.PersonType?.Subclass is null || x.PersonType.Subclass.Equals("OrganizationUser", StringComparison.OrdinalIgnoreCase))
            .Select(x => new MicrosoftPerson(x.Id ?? "", x.DisplayName!.Trim(), x.ScoredEmailAddresses.FirstOrDefault()?.Address?.Trim() ?? "", x.JobTitle?.Trim() ?? ""))
            .DistinctBy(x => x.Email.Length > 0 ? x.Email : x.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.DisplayName).ToList();
    }

    private sealed class PeoplePage { [JsonPropertyName("value")] public List<GraphPerson> Value { get; init; } = []; }
    private sealed class GraphPerson
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? JobTitle { get; init; }
        public List<ScoredAddress> ScoredEmailAddresses { get; init; } = [];
        public PersonKind? PersonType { get; init; }
    }
    private sealed class ScoredAddress { public string? Address { get; init; } }
    private sealed class PersonKind { public string? Subclass { get; init; } }
}
