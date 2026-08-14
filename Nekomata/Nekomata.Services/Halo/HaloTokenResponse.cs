using System.Text.Json.Serialization;

namespace Nekomata.Services.Halo;

internal sealed class HaloTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("expires_in")]
    public int ExpiresInSeconds { get; set; }
}