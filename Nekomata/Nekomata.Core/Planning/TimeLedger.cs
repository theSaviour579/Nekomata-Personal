namespace Nekomata.Core.Planning;

public enum TimeCategory { Meetings, Email, Chat, Calls, Focus, Projects, Documents, Admin, Browsing, Other, Away }

/// <summary>One stretch of time in one category (minutes are whole, merged per sample).</summary>
public sealed class TimeEntry
{
    public DateTime Date { get; set; }
    public Dictionary<TimeCategory, double> Minutes { get; set; } = [];
    public DateTime? FirstActive { get; set; }
    public DateTime? LastActive { get; set; }
    /// <summary>Longest gap with no input between 11:30 and 14:30, in minutes (a lunch break).</summary>
    public double LongestMiddayBreak { get; set; }
    public DateTime? LastInputAt { get; set; }
    public DateTime? MiddayGapStart { get; set; }
}

/// <summary>
/// Where the day goes. Samples of the foreground app and window title (every few seconds) become categories: a meeting
/// in the calendar or a call wins, then a running focus mission, then the app (Outlook = email, Teams = chat, Word/Excel
/// = documents, Visual Studio/terminals = projects...). Time with no keyboard or mouse for five minutes is "away".
/// Reactive = email, chat, calls; planned = focus, projects,
/// documents. Pure, so it's tested without Windows.
/// </summary>
public static class TimeLedger
{
    public static readonly TimeCategory[] Reactive = [TimeCategory.Email, TimeCategory.Chat, TimeCategory.Calls];
    public static readonly TimeCategory[] Planned = [TimeCategory.Focus, TimeCategory.Projects, TimeCategory.Documents];

    public static TimeCategory Categorise(string app, string title, bool inMeeting, bool onCall, bool focusMission, bool idle)
    {
        if (idle) return inMeeting ? TimeCategory.Meetings : TimeCategory.Away;
        if (onCall) return TimeCategory.Calls;
        if (inMeeting) return TimeCategory.Meetings;
        var a = app.ToLowerInvariant();
        var t = title.ToLowerInvariant();
        if (focusMission) return TimeCategory.Focus;
        if (a is "outlook" or "olk" || t.EndsWith("- outlook") || t.Contains("outlook.office") || t.Contains("mail -")) return TimeCategory.Email;
        if (a is "ms-teams" or "teams" || t.Contains("microsoft teams")) return t.Contains("meeting") || t.Contains("call") ? TimeCategory.Meetings : TimeCategory.Chat;
        if (a is "zoom" or "webex" or "slack") return t.Contains("meeting") || t.Contains("call") ? TimeCategory.Meetings : TimeCategory.Chat;
        if (a is "winword" or "excel" or "powerpnt" or "onenote" or "acrobat" or "acrord32" || t.Contains("sharepoint") || t.Contains("word") && a == "msedge") return TimeCategory.Documents;
        if (a is "devenv" or "code" or "windowsterminal" or "powershell" or "pwsh" or "cmd" or "mstsc" or "mmc" or "ssms" or "pgadmin4" or "putty" or "winscp" or "rider64" or "dbeaver")
            return TimeCategory.Projects;
        if (t.Contains("admin center") || t.Contains("admin centre") || t.Contains("portal") || t.Contains("settings"))
            return TimeCategory.Admin;
        if (a is "msedge" or "chrome" or "firefox" or "brave") return TimeCategory.Browsing;
        if (a is "nekomata" or "nekomata.ui") return TimeCategory.Admin;
        return TimeCategory.Other;
    }

    /// <summary>Adds one sample to the day.</summary>
    public static void Add(TimeEntry day, DateTime at, double minutes, TimeCategory category)
    {
        day.Minutes[category] = day.Minutes.GetValueOrDefault(category) + minutes;
        if (category == TimeCategory.Away) return;
        day.FirstActive ??= at;
        day.LastActive = at;
    }

    /// <summary>Tracks the longest gap in input around lunchtime.</summary>
    public static void NoteInput(TimeEntry day, DateTime lastInput)
    {
        if (day.LastInputAt is { } previous && lastInput > previous)
        {
            var gapStart = previous;
            var gapEnd = lastInput;
            var windowStart = previous.Date.AddHours(11.5);
            var windowEnd = previous.Date.AddHours(14.5);
            var overlap = (Min(gapEnd, windowEnd) - Max(gapStart, windowStart)).TotalMinutes;
            if (overlap > day.LongestMiddayBreak) day.LongestMiddayBreak = overlap;
        }
        day.LastInputAt = lastInput;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    public static double Total(TimeEntry day) => day.Minutes.Where(m => m.Key != TimeCategory.Away).Sum(m => m.Value);

    public static int ReactivePercent(IEnumerable<TimeEntry> days)
    {
        var list = days.ToList();
        var reactive = list.Sum(d => Reactive.Sum(c => d.Minutes.GetValueOrDefault(c)));
        var planned = list.Sum(d => Planned.Sum(c => d.Minutes.GetValueOrDefault(c)));
        return reactive + planned < 1 ? 0 : (int)Math.Round(100 * reactive / (reactive + planned));
    }

    public static string Hours(double minutes) => minutes < 60 ? $"{minutes:0}m" : $"{(int)(minutes / 60)}h {minutes % 60:00}m";

    public static string Summary(IReadOnlyList<TimeEntry> days)
    {
        var totals = new Dictionary<TimeCategory, double>();
        foreach (var d in days)
            foreach (var (k, v) in d.Minutes)
                if (k != TimeCategory.Away) totals[k] = totals.GetValueOrDefault(k) + v;
        var all = totals.Values.Sum();
        if (all < 1) return "Nothing recorded yet.";
        return string.Join("\n", totals.OrderByDescending(t => t.Value).Where(t => t.Value >= 1)
            .Select(t => $"{Label(t.Key),-14} {Hours(t.Value),8}  {100 * t.Value / all,3:0}%"));
    }

    public static string Label(TimeCategory c) => c switch
    {
        TimeCategory.Chat => "Teams chat", TimeCategory.Focus => "Focus blocks",
        _ => c.ToString()
    };
}
