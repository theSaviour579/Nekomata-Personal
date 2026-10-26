namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public interface IMicrosoftAuthenticationService
{
    Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// A token for extra scopes asked for on their own (incremental consent), so an optional feature's permission never
    /// affects the main sign-in. Interactive only when the user asked to enable the feature.
    /// </summary>
    Task<TokenResult> GetTokenForScopesAsync(string[] scopes, bool interactive, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Additional scopes are not supported by this sign-in.");

    /// <summary>Silent-first token for extra scopes, asking interactively only when needed.</summary>
    Task<TokenResult> GetTokenForScopesAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) =>
        GetTokenAsync(cancellationToken);

    /// <summary>The connected (primary) account, or null when nobody is signed in.</summary>
    Task<string?> GetConnectedAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    /// <summary>Signs every cached account out of this app.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>The cached accounts, which is the everyday (primary) one, and which named permission sets each holds silently.</summary>
    Task<IReadOnlyList<SignedInAccount>> GetSignedInAccountsAsync(IReadOnlyDictionary<string, string[]> permissionSets, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SignedInAccount>>([]);

    /// <summary>Makes this account the one used for everyday calls (mail, calendar, Teams).</summary>
    Task SetPrimaryAccountAsync(string username) => Task.CompletedTask;

    /// <summary>Interactive sign-in with an account picker, for these scopes. Returns the account used.</summary>
    Task<string> SignInAsync(string[] scopes, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Interactive sign-in is not supported by this sign-in.");

    /// <summary>Removes the account from the token cache.</summary>
    Task SignOutAsync(string username) => Task.CompletedTask;
}

public sealed record SignedInAccount(string Username, bool IsPrimary, IReadOnlyDictionary<string, bool> Permissions);