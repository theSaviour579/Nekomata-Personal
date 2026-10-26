using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>
/// Text prepared for the Kokoro voice. Its pronunciation step drops words containing apostrophes and reads a lone
/// capital "A" as the word "a" (so "S L A" came out as "S L"). Contractions are written out, possessive apostrophes
/// removed, and acronyms spelled with letter names ("KPI" → "kay pee eye"). Run after GuardianSpeechTextNormalizer.
/// </summary>
public static partial class KokoroText
{
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['A'] = "ay", ['B'] = "bee", ['C'] = "see", ['D'] = "dee", ['E'] = "ee", ['F'] = "eff", ['G'] = "jee", ['H'] = "aitch",
        ['I'] = "eye", ['J'] = "jay", ['K'] = "kay", ['L'] = "ell", ['M'] = "em", ['N'] = "en", ['O'] = "oh", ['P'] = "pee",
        ['Q'] = "queue", ['R'] = "ar", ['S'] = "ess", ['T'] = "tee", ['U'] = "you", ['V'] = "vee", ['W'] = "double-you",
        ['X'] = "ex", ['Y'] = "why", ['Z'] = "zed",
    };

    /// <summary>All-capital words that are said as words, not letters.</summary>
    private static readonly HashSet<string> SaidAsWords = new(StringComparer.Ordinal)
    {
        "OK", "NATO", "RAM", "ROM", "SIM", "PIN", "LAN", "WAN", "GIF", "JPEG", "ASAP", "SAGE", "TEAMS", "NOW", "TODAY",
        "NOTE", "FYI", "WIFI",
    };

    private static readonly (Regex Pattern, string Replacement)[] Contractions =
    [
        (new(@"\bwon't\b", RegexOptions.IgnoreCase), "will not"),
        (new(@"\bcan't\b", RegexOptions.IgnoreCase), "cannot"),
        (new(@"\bshan't\b", RegexOptions.IgnoreCase), "shall not"),
        (new(@"\bain't\b", RegexOptions.IgnoreCase), "is not"),
        (new(@"\blet's\b", RegexOptions.IgnoreCase), "let us"),
        (new(@"\b(\w+)n't\b", RegexOptions.IgnoreCase), "$1 not"),
        (new(@"\bI'm\b", RegexOptions.None), "I am"),
        (new(@"\b(it|that|there|here|what|who|where|how|he|she|everything|nothing|something|this)'s\b", RegexOptions.IgnoreCase), "$1 is"),
        (new(@"\b(\w+)'re\b", RegexOptions.IgnoreCase), "$1 are"),
        (new(@"\b(\w+)'ve\b", RegexOptions.IgnoreCase), "$1 have"),
        (new(@"\b(\w+)'ll\b", RegexOptions.IgnoreCase), "$1 will"),
        (new(@"\b(I|you|he|she|we|they|it|that|there|who)'d\b", RegexOptions.IgnoreCase), "$1 would"),
        (new(@"\bo'clock\b", RegexOptions.IgnoreCase), "oclock"),
    ];

    /// <summary>Names Kokoro says wrongly as written ("Copilot" came out as "copillot").</summary>
    private static readonly (Regex Pattern, string Said)[] Pronunciations =
    [
        (new(@"\bCopilot\b", RegexOptions.IgnoreCase), "co pilot"),
        (new(@"\bAutopilot\b", RegexOptions.IgnoreCase), "auto pilot"),
        (new(@"\bSharePoint\b", RegexOptions.IgnoreCase), "share point"),
        (new(@"\bOneDrive\b", RegexOptions.IgnoreCase), "one drive"),
        (new(@"\bPowerShell\b", RegexOptions.IgnoreCase), "power shell"),
        (new(@"\bNekomata\b", RegexOptions.IgnoreCase), "neko mata"),
    ];

    public static string Prepare(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        var s = text.Replace('’', '\'').Replace('‘', '\'');
        foreach (var (pattern, said) in Pronunciations) s = pattern.Replace(s, said);
        foreach (var (pattern, replacement) in Contractions) s = pattern.Replace(s, replacement);
        s = Regex.Replace(s, @"(\w)'s\b", "$1s");          // Sam's → Sams (said the same)
        s = Regex.Replace(s, @"(\w)s'(?=\s|$|[.,;:!?])", "$1s"); // the teams' → the teams
        s = s.Replace("'", "");

        // Letters already spaced out by the general normaliser ("K P I") and plain acronyms ("PDF", "PDFs").
        s = SpacedLetters().Replace(s, m => string.Join(" ", m.Value.Split(' ').Select(l => Letters[l[0]])));
        s = Acronym().Replace(s, m =>
        {
            var word = m.Groups["w"].Value;
            if (SaidAsWords.Contains(word)) return word == "OK" ? "okay" : m.Value;
            return string.Join(" ", word.Select(c => Letters[c])) + (m.Groups["s"].Success ? "s" : "");
        });
        s = s.Replace("&", " and ").Replace("%", " percent").Replace("/", " or ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    [GeneratedRegex(@"\b[A-Z](?: [A-Z]\b)+")]
    private static partial Regex SpacedLetters();

    [GeneratedRegex(@"\b(?<w>[A-Z]{2,5})(?<s>s)?\b")]
    private static partial Regex Acronym();
}
