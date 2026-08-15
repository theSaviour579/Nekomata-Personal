using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Nekomata.Integrations.MicrosoftGraph.Authentication;

public sealed class MicrosoftAuthenticationService : IMicrosoftAuthenticationService
{
    private const string CacheFileName = "msal-user-cache.bin";
    private readonly MicrosoftGraphOptions _options;
    private readonly IPublicClientApplication _application;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly MsalCacheHelper? _cacheHelper;

    public MicrosoftAuthenticationService(MicrosoftGraphOptions options)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("Microsoft Graph is not configured. Add MicrosoftGraph:ClientId to user secrets or appsettings.json.");

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
            var account = (await _application.GetAccountsAsync()).FirstOrDefault();

            try
            {
                result = account is null
                    ? throw new MsalUiRequiredException("no_account", "No cached Microsoft account is available.")
                    : await _application.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                result = await _application
                    .AcquireTokenInteractive(_options.Scopes)
                    .ExecuteAsync(cancellationToken);
            }

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
