namespace Nekomata.Integrations.MicrosoftGraph.Profile;

public sealed record MicrosoftUserProfile(string DisplayName, string JobTitle);

public interface IMicrosoftUserProfileService
{
    Task<MicrosoftUserProfile?> GetAsync(CancellationToken cancellationToken = default);
}
