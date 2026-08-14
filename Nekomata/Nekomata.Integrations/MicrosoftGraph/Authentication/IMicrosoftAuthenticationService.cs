namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public interface IMicrosoftAuthenticationService
{
    Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default);
}