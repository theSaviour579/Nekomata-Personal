namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /// <summary>"23:16" under an hour, "1:05:30" over it: short enough to sit inside the focus core.</summary>
    private static string MissionClock(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = span.Negate();
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";
    }
}
