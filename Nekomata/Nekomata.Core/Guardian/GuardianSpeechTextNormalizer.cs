using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

public static class GuardianSpeechTextNormalizer
{
    private static readonly (string Pattern, string Replacement, RegexOptions Options)[] BusinessTerms =
    [
        (@"\bYoY\b", "year on year", RegexOptions.IgnoreCase),
        (@"\bMoM\b", "month on month", RegexOptions.None),
        (@"\bQoQ\b", "quarter on quarter", RegexOptions.IgnoreCase),
        (@"\bP1\b", "priority one", RegexOptions.IgnoreCase),
        (@"\bP2\b", "priority two", RegexOptions.IgnoreCase),
        (@"\bSLA\b", "S L A", RegexOptions.None),
        (@"\bKPI\b", "K P I", RegexOptions.None),
        (@"\bROI\b", "return on investment", RegexOptions.None),
        (@"\bSQL\b", "S Q L", RegexOptions.None),
        (@"\bAPI\b", "A P I", RegexOptions.None),
        (@"\bOOO\b", "out of office", RegexOptions.None),
        (@"\bCC'd\b", "copied", RegexOptions.IgnoreCase),
        (@"\bIT\b", "I T", RegexOptions.None)
    ];

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var value = Regex.Replace(text, @"https?://\S+", "a linked resource");
        value = Regex.Replace(value, @"\bticket\s*#\s*(\d+)", "ticket $1", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"#\s*(\d+)", "ticket $1");
        value = Regex.Replace(value, @"\u00A3\s*([\d,]+(?:\.\d{1,2})?)", "$1 pounds");
        value = Regex.Replace(value, @"\b(\d+):(\d{2}):(\d{2})\b", match =>
            $"{Pluralise(match.Groups[1].Value, "hour")}, {Pluralise(match.Groups[2].Value, "minute")}, and {Pluralise(match.Groups[3].Value, "second")}");
        value = Regex.Replace(value, @"\b(\d+)\s*h\b", match => Pluralise(match.Groups[1].Value, "hour"), RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b(\d+)\s*m\b", match => Pluralise(match.Groups[1].Value, "minute"), RegexOptions.IgnoreCase);

        foreach (var (pattern, replacement, options) in BusinessTerms)
            value = Regex.Replace(value, pattern, replacement, options);

        value = value.Replace("\u2022", ". ").Replace("\u2014", ", ").Replace("\u00B7", ", ");
        value = Regex.Replace(value, @"\s*[\u002D\u2013]\s*", ", ");
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length <= 900 ? value : value[..897] + "...";
    }

    private static string Pluralise(string number, string unit)
    {
        var value = int.Parse(number);
        return $"{value} {unit}{(value == 1 ? string.Empty : "s")}";
    }
}