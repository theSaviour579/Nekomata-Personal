using System.IO;

namespace Nekomata.UI.Services;

public sealed class FirstRunService
{
    private readonly PersonalProfileService _profile;

    public FirstRunService(PersonalProfileService profile)
    {
        _profile = profile;
    }

    public bool IsFirstRun => !_profile.IsConfigured || !_profile.Current.WorkScheduleConfigured;

    public void Complete(
        string displayName,
        bool startWithWindows,
        TimeSpan workdayStart,
        TimeSpan workdayEnd,
        bool includeLunchBreak,
        TimeSpan lunchStart,
        TimeSpan lunchEnd,
        TimeSpan wrapUpTime,
        bool emailBriefingEnabled)
    {
        _profile.SaveWorkSchedule(
            workdayStart, workdayEnd, includeLunchBreak, lunchStart, lunchEnd,
            wrapUpTime, emailBriefingEnabled);
        _profile.Save(displayName, startWithWindows);
    }
}
