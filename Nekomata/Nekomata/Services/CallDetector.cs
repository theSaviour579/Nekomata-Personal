using System.IO;
using Microsoft.Win32;

namespace Nekomata.UI.Services;

/// <summary>
/// Is another app using the microphone right now? That is the most reliable local sign of a live call (Teams, Zoom,
/// Zoom, a browser meeting) and it's instant, unlike Teams presence, which lags and can't see other apps. Windows records
/// mic use per app under CapabilityAccessManager\ConsentStore\microphone: an app with a start time and no stop time
/// has the mic open now. Nekomata's own listening is ignored.
/// </summary>
public static class CallDetector
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// <summary>The app holding the microphone ("Teams", "Zoom", "a browser"...), or null.</summary>
    public static string? OtherAppUsingMicrophone()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Root);
            if (root is null) return null;
            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    using var nonPackaged = root.OpenSubKey(name);
                    if (nonPackaged is null) continue;
                    foreach (var app in nonPackaged.GetSubKeyNames())
                        if (InUse(nonPackaged, app) && !IsSelf(app)) return Friendly(app);
                }
                else if (InUse(root, name) && !IsSelf(name)) return Friendly(name);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Microphone check failed: {ex.Message}"); }
        return null;
    }

    private static bool InUse(RegistryKey parent, string name)
    {
        using var key = parent.OpenSubKey(name);
        if (key is null) return false;
        var start = key.GetValue("LastUsedTimeStart") is long s ? s : 0;
        var stop = key.GetValue("LastUsedTimeStop") is long e ? e : -1;
        return start > 0 && stop == 0;
    }

    private static bool IsSelf(string name) =>
        name.Contains("Nekomata", StringComparison.OrdinalIgnoreCase) ||
        name.Contains(Path.GetFileName(Environment.ProcessPath ?? "Nekomata.exe"), StringComparison.OrdinalIgnoreCase);

    private static string Friendly(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("teams")) return "Teams";
        if (n.Contains("zoom")) return "Zoom";
        if (n.Contains("webex")) return "Webex";
        if (n.Contains("slack")) return "Slack";
        if (n.Contains("chrome") || n.Contains("msedge") || n.Contains("firefox") || n.Contains("brave")) return "a browser";
        var file = name.Split('#').LastOrDefault() ?? name;
        return Path.GetFileNameWithoutExtension(file.Split('_')[0]);
    }
}
