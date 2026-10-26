using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Nekomata.Models.Integrations;

namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public sealed class MicrosoftAuthenticationService : IMicrosoftAuthenticationService
{
    private const string CacheFileName = "msal-user-cache.bin";
    private readonly MicrosoftGraphOptions _options;
    private readonly IPublicClientApplication? _application;
    private readonly IConfidentialClientApplication? _confidentialApplication;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly MsalCacheHelper? _cacheHelper;

    public MicrosoftAuthenticationService(MicrosoftGraphOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("Microsoft Graph is not configured. Add MicrosoftGraph:ClientId to user secrets or appsettings.json.");

        if(options.UsesClientCredentials)
        {
            if(string.IsNullOrWhiteSpace(options.TenantId)||string.IsNullOrWhiteSpace(options.ClientSecret)||string.IsNullOrWhiteSpace(options.UserId))
                throw new UnattendedAuthenticationException(AuthenticationFailureKind.CredentialUnavailable,"Microsoft Graph");
            _confidentialApplication=ConfidentialClientApplicationBuilder.Create(options.ClientId)
                .WithTenantId(options.TenantId).WithClientSecret(options.ClientSecret).Build();
            return;
        }
        var builder = PublicClientApplicationBuilder
            .Create(options.ClientId)
            .WithRedirectUri(options.RedirectUri);

        _application = string.IsNullOrWhiteSpace(options.TenantId)
            ? builder.WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount).Build()
            : builder.WithTenantId(options.TenantId).Build();

        _cacheHelper = ConfigurePersistentCache(_application);
    }

    public async Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            AuthenticationResult result;
            if(_confidentialApplication is not null)
            {
                try { result=await _confidentialApplication.AcquireTokenForClient(["https://graph.microsoft.com/.default"]).ExecuteAsync(cancellationToken); }
                catch(MsalServiceException ex){throw Map(ex);}
                return new(){AccessToken=result.AccessToken,ExpiresOn=result.ExpiresOn,AccountName=_options.UserId};
            }
            var application=_application!;
            var account = await PrimaryAccountAsync(application);

            try
            {
                result = account is null
                    ? throw new MsalUiRequiredException("no_account", "No cached Microsoft account is available.")
                    : await application.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException) when (_options.AllowInteractiveAuthentication)
            {
                var interactive = application.AcquireTokenInteractive(_options.Scopes);
                if (account is not null) interactive = interactive.WithAccount(account);
                result = await interactive.ExecuteAsync(cancellationToken);
            }
            catch(MsalUiRequiredException ex)
            { throw new UnattendedAuthenticationException(AuthenticationFailureKind.AuthenticationRequired,"Microsoft Graph",ex); }
            catch(MsalServiceException ex){throw Map(ex);}

            RememberPrimary(result.Account);
            return new TokenResult
            {
                AccessToken = result.AccessToken,
                ExpiresOn = result.ExpiresOn,
                AccountName = result.Account?.Username ?? string.Empty
            };
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<TokenResult> GetTokenForScopesAsync(string[] scopes, bool interactive, CancellationToken cancellationToken = default)
    {
        if (_application is null)
            throw new UnattendedAuthenticationException(AuthenticationFailureKind.ConsentMissing, "Microsoft Graph");
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // The extra permissions may have been granted while signed in as a different account (an admin account, say),
            // so the account that last granted these scopes is tried first, then every cached account, before giving up.
            var key = string.Join(" ", scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            var accounts = (await _application.GetAccountsAsync()).ToList();
            if (_scopeAccounts.TryGetValue(key, out var preferred))
                accounts = accounts.OrderByDescending(a => a.HomeAccountId?.Identifier == preferred).ToList();
            MsalUiRequiredException? lastUiRequired = null;
            foreach (var candidate in accounts)
            {
                try
                {
                    var silent = await _application.AcquireTokenSilent(scopes, candidate).ExecuteAsync(cancellationToken);
                    if (silent.Account?.HomeAccountId?.Identifier is { } id) _scopeAccounts[key] = id;
                    return new TokenResult { AccessToken = silent.AccessToken, ExpiresOn = silent.ExpiresOn, AccountName = silent.Account?.Username ?? "" };
                }
                catch (MsalUiRequiredException ex) { lastUiRequired = ex; }
                catch (MsalServiceException ex) { throw Map(ex); }
            }
            if (!interactive)
                throw new UnattendedAuthenticationException(AuthenticationFailureKind.ConsentMissing, "Microsoft Graph",
                    lastUiRequired ?? new MsalUiRequiredException("no_account", "No cached Microsoft account is available."));
            try
            {
                var builder = _application.AcquireTokenInteractive(scopes);
                if (await PrimaryAccountAsync(_application) is { } primary) builder = builder.WithAccount(primary);
                var result = await builder.ExecuteAsync(cancellationToken);
                if (result.Account?.HomeAccountId?.Identifier is { } id) _scopeAccounts[key] = id;
                return new TokenResult { AccessToken = result.AccessToken, ExpiresOn = result.ExpiresOn, AccountName = result.Account?.Username ?? "" };
            }
            catch (MsalServiceException ex) { throw Map(ex); }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Silent-first token for an extra set of scopes; interactive consent when needed (Personal: optional features).</summary>
    public Task<TokenResult> GetTokenForScopesAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) =>
        GetTokenForScopesAsync(scopes.ToArray(), _options.AllowInteractiveAuthentication, cancellationToken);

    public async Task<string?> GetConnectedAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null) return _options.UsesClientCredentials ? _options.UserId : null;
        await _tokenLock.WaitAsync(cancellationToken);
        try { return (await PrimaryAccountAsync(_application))?.Username; }
        finally { _tokenLock.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null) return;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var account in await _application.GetAccountsAsync())
                await _application.RemoveAsync(account);
            _primaryAccountId = null;
            try { File.Delete(PrimaryAccountPath); } catch (IOException) { }
        }
        finally { _tokenLock.Release(); }
    }

    private readonly Dictionary<string, string> _scopeAccounts = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SignedInAccount>> GetSignedInAccountsAsync(IReadOnlyDictionary<string, string[]> permissionSets, CancellationToken cancellationToken = default)
    {
        if (_application is null) return [];
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            var primary = await PrimaryAccountAsync(_application);
            var list = new List<SignedInAccount>();
            foreach (var account in await _application.GetAccountsAsync())
            {
                var granted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, scopes) in permissionSets)
                {
                    try { await _application.AcquireTokenSilent(scopes, account).ExecuteAsync(cancellationToken); granted[name] = true; }
                    catch (MsalException) { granted[name] = false; }
                }
                list.Add(new(account.Username ?? "(unknown)", account.HomeAccountId?.Identifier == primary?.HomeAccountId?.Identifier, granted));
            }
            return list.OrderByDescending(a => a.IsPrimary).ThenBy(a => a.Username).ToList();
        }
        finally { _tokenLock.Release(); }
    }

    public async Task SetPrimaryAccountAsync(string username)
    {
        if (_application is null) return;
        var account = (await _application.GetAccountsAsync()).FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        var id = account?.HomeAccountId?.Identifier;
        if (string.IsNullOrWhiteSpace(id)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(PrimaryAccountPath)!);
        File.WriteAllText(PrimaryAccountPath, id);
        _primaryAccountId = id;
    }

    public async Task<string> SignInAsync(string[] scopes, CancellationToken cancellationToken = default)
    {
        if (_application is null) throw new InvalidOperationException("Interactive sign-in isn't available with an app-only connection.");
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            var result = await _application.AcquireTokenInteractive(scopes.Length > 0 ? scopes : _options.Scopes)
                .WithPrompt(Prompt.SelectAccount).ExecuteAsync(cancellationToken);
            if (scopes.Length > 0 && result.Account?.HomeAccountId?.Identifier is { } id)
                _scopeAccounts[string.Join(" ", scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))] = id;
            return result.Account?.Username ?? "";
        }
        catch (MsalServiceException ex) { throw Map(ex); }
        finally { _tokenLock.Release(); }
    }

    public async Task SignOutAsync(string username)
    {
        if (_application is null) return;
        var account = (await _application.GetAccountsAsync()).FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        if (account is null) return;
        await _application.RemoveAsync(account);
        if (account.HomeAccountId?.Identifier == _primaryAccountId)
        {
            _primaryAccountId = null;
            try { File.Delete(PrimaryAccountPath); } catch (IOException) { }
        }
    }

    // Everyday Graph calls (mail, calendar, Teams, presence) must always be the user's own account, even after an extra
    // permission was granted by signing in with another (an admin account). The first account that gets a main-scope token
    // is remembered as primary; with several cached and none remembered, an account that doesn't look like an admin one wins.
    private static string PrimaryAccountPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "Authentication", "primary-account.txt");
    private string? _primaryAccountId;

    private async Task<IAccount?> PrimaryAccountAsync(IPublicClientApplication application)
    {
        var accounts = (await application.GetAccountsAsync()).ToList();
        if (accounts.Count == 0) return null;
        _primaryAccountId ??= File.Exists(PrimaryAccountPath) ? File.ReadAllText(PrimaryAccountPath).Trim() : null;
        var remembered = accounts.FirstOrDefault(a => a.HomeAccountId?.Identifier == _primaryAccountId);
        if (remembered is not null) return remembered;
        if (accounts.Count == 1) return accounts[0];
        static bool LooksAdmin(IAccount a) =>
            (a.Username ?? "").Split('@')[0] is var user && (user.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                                                             user.StartsWith("adm", StringComparison.OrdinalIgnoreCase) ||
                                                             user.EndsWith("-a", StringComparison.OrdinalIgnoreCase) ||
                                                             user.EndsWith(".a", StringComparison.OrdinalIgnoreCase));
        return accounts.FirstOrDefault(a => !LooksAdmin(a)) ?? accounts[0];
    }

    private void RememberPrimary(IAccount? account)
    {
        var id = account?.HomeAccountId?.Identifier;
        if (string.IsNullOrWhiteSpace(id) || id == _primaryAccountId) return;
        if (_primaryAccountId is not null && File.Exists(PrimaryAccountPath)) return; // first one wins; never switch silently
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrimaryAccountPath)!);
            File.WriteAllText(PrimaryAccountPath, id);
            _primaryAccountId = id;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Primary account not saved: {ex.Message}"); }
    }

    private static UnattendedAuthenticationException Map(MsalServiceException error)
    {
        var text=$"{error.ErrorCode} {error.Message}";
        var kind=text.Contains("65001",StringComparison.OrdinalIgnoreCase)||text.Contains("consent",StringComparison.OrdinalIgnoreCase)
            ?AuthenticationFailureKind.ConsentMissing
            :text.Contains("expired",StringComparison.OrdinalIgnoreCase)||text.Contains("invalid_grant",StringComparison.OrdinalIgnoreCase)
                ?AuthenticationFailureKind.TokenExpired
                :AuthenticationFailureKind.CredentialUnavailable;
        return new(kind,"Microsoft Graph",error);
    }

    private static MsalCacheHelper? ConfigurePersistentCache(IPublicClientApplication application)
    {
        try
        {
            var cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nekomata Personal",
                "Authentication");

            var storageProperties = new StorageCreationPropertiesBuilder(CacheFileName, cacheDirectory)
                .Build();
            var cacheHelper = MsalCacheHelper.CreateAsync(storageProperties)
                .GetAwaiter()
                .GetResult();
            cacheHelper.RegisterCache(application.UserTokenCache);
            return cacheHelper;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Persistent Microsoft token cache unavailable; using the in-memory cache: {ex}");
            return null;
        }
    }
}
