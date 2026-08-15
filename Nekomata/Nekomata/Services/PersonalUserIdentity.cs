using Nekomata.Core.Personalization;

namespace Nekomata.UI.Services;

public sealed class PersonalUserIdentity(PersonalProfileService profile) : IUserIdentity
{
    public string DisplayName => string.IsNullOrWhiteSpace(profile.Current.DisplayName)
        ? "there"
        : profile.Current.DisplayName.Trim();
}
