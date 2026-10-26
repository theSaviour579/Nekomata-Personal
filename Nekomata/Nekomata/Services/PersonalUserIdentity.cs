using Nekomata.Core.Personalization;

namespace Nekomata.UI.Services;

public sealed class PersonalUserIdentity(PersonalProfileService profile) : IUserIdentity
{
    private string Name => profile.Current.DisplayName.Trim();

    public string DisplayName => Name.Length == 0 ? "there" : Name;

    public string FirstName => Name.Length == 0
        ? ""
        : Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    public string Description
    {
        get
        {
            var who = Name.Length == 0 ? "the user" : Name;
            var title = profile.Current.JobTitle.Trim();
            return title.Length == 0 ? who : $"{who}, {title}";
        }
    }

    public bool Is(string? nameOrAddress)
    {
        if (string.IsNullOrWhiteSpace(nameOrAddress) || Name.Length == 0) return false;
        var value = nameOrAddress.Trim();
        if (value.Equals(Name, StringComparison.OrdinalIgnoreCase)) return true;
        var first = FirstName;
        return first.Length > 0 && value.Contains(first, StringComparison.OrdinalIgnoreCase) &&
               value.Contains(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1], StringComparison.OrdinalIgnoreCase);
    }
}
