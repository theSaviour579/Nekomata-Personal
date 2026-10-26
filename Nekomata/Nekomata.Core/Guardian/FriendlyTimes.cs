using System.Globalization;
using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>
/// Machine timestamps in text the user reads ("2026-11-04T09:00:00+00:00 to 2026-11-04T11:00:00+00:00") become plain
/// English in local time ("Wed 4 Nov, 09:00–11:00"). The structured calendar changes keep their exact ISO values;
/// only the words shown and spoken are tidied.
/// </summary>
public static partial class FriendlyTimes
{
    public static string Humanise(string? text, DateTime? today = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('T')) return text ?? "";
        var year = (today ?? DateTime.Today).Year;
        var s = Range().Replace(text, m =>
        {
            if (!TryLocal(m.Groups["a"].Value, out var a) || !TryLocal(m.Groups["b"].Value, out var b)) return m.Value;
            return a.Date == b.Date ? $"{Day(a, year)}, {a:HH:mm}–{b:HH:mm}" : $"{Day(a, year)} {a:HH:mm} to {Day(b, year)} {b:HH:mm}";
        });
        return Single().Replace(s, m => TryLocal(m.Value, out var t) ? $"{Day(t, year)} {t:HH:mm}" : m.Value);
    }

    private static bool TryLocal(string iso, out DateTime local)
    {
        local = default;
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)) return false;
        local = value.LocalDateTime;
        return true;
    }

    private static string Day(DateTime d, int year) =>
        d.Year == year ? d.ToString("ddd d MMM", CultureInfo.GetCultureInfo("en-GB")) : d.ToString("ddd d MMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    private const string Iso = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?";

    [GeneratedRegex("(?<a>" + Iso + @")\s*(?:to|until|-|–|—)\s*(?<b>" + Iso + ")")]
    private static partial Regex Range();

    [GeneratedRegex(Iso)]
    private static partial Regex Single();
}
