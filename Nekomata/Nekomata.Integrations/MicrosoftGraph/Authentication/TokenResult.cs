namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public class TokenResult
{
    public string AccessToken { get; init; } = "";

    public DateTimeOffset ExpiresOn { get; init; }

    public string AccountName { get; init; } = "";
}