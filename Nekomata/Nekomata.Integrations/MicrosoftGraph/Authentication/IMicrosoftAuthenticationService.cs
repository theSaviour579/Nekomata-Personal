namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public interface IMicrosoftAuthenticationService
{
    Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default);
    Task<string?> GetConnectedAccountAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
