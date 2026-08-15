using System.IO;

namespace Nekomata.UI.Services;

public sealed class FirstRunService
{
    private readonly PersonalProfileService _profile;

    public FirstRunService(PersonalProfileService profile)
    {
        _profile = profile;
    }

    public bool IsFirstRun => !_profile.IsConfigured;

    public void Complete(string displayName, bool startWithWindows)
    {
        _profile.Save(displayName, startWithWindows);
    }
}
