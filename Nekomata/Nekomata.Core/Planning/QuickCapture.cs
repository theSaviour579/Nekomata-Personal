using System.Text.RegularExpressions;

namespace Nekomata.Core.Planning;

/// <summary>
/// Capture from anywhere: a Teams message or an email the user sends himself that starts "G:" (or "Guardian:") is an
/// instruction to Guardian. This finds the text after the prefix; the AI then decides what it is (task, reminder, note).
/// </summary>
public static partial class QuickCapture
{
    public static string? Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = TagRegex().Replace(text, " ");
        plain = System.Net.WebUtility.HtmlDecode(plain).Trim();
        var m = PrefixRegex().Match(plain);
        if (!m.Success) return null;
        var rest = SpaceRegex().Replace(m.Groups["t"].Value, " ").Trim();
        return rest.Length >= 3 ? rest : null;
    }

    [GeneratedRegex(@"^(?:g|guardian)\s*[:>\-–]\s*(?<t>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PrefixRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}

/// <summary>Where the user is, from the network he's on: the office if any current gateway, DNS suffix or Wi-Fi name is one he
/// has marked as the office ("I'm in the office"), home likewise, otherwise unknown.</summary>
public static class NetworkPlace
{
    public static string Decide(IEnumerable<string> currentSignals, IEnumerable<string> office, IEnumerable<string> home)
    {
        var now = currentSignals.Where(s => s.Length > 0).Select(s => s.ToLowerInvariant()).ToHashSet();
        if (office.Any(o => now.Contains(o.ToLowerInvariant()))) return "office";
        if (home.Any(h => now.Contains(h.ToLowerInvariant()))) return "home";
        return "";
    }
}
