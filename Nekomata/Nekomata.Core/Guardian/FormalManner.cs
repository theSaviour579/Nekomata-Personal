using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>
/// Guardian's manner when it speaks to the user: formal and composed, in the manner of a discreet British personal
/// assistant, calling him "Sir" about once an utterance (never every sentence). Applied to what Guardian says aloud,
/// and given to the AI as an instruction for its replies to him. Never applied to drafts written in the user's own voice.
/// </summary>
public static partial class FormalManner
{
    /// <summary>On by default; the desktop sets it from settings ("formal-voice").</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>The user's first name: dropped where a line addresses him by it, since "Sir" takes its place.</summary>
    public static string UserFirstName { get; set; } = "";

    public const string Instruction =
        "Manner: speak to the user as a composed, formal British personal assistant would: courteous, precise, understated, " +
        "occasionally dry. Address him as \"Sir\", about once in a reply (at the start or end), never in every sentence. " +
        "Prefer \"Very good\", \"Certainly\", \"Right away\" and \"I'm afraid\" to \"OK\", \"Sure\" or \"Sorry\". No exclamation " +
        "marks and no gushing. This applies only to what you say to him, never to emails, messages or documents you draft for him.";

    private static readonly (Regex Pattern, string Replacement)[] Openers =
    [
        (new(@"^(?:good\s+)?morning\b[.,!]+\s*", RegexOptions.IgnoreCase), "Good morning, Sir. "),
        (new(@"^(?:good\s+)?afternoon\b[.,!]+\s*", RegexOptions.IgnoreCase), "Good afternoon, Sir. "),
        (new(@"^(?:good\s+)?evening\b[.,!]+\s*", RegexOptions.IgnoreCase), "Good evening, Sir. "),
        (new(@"^(?:okay|ok|sure|fine|right|got it|all right|alright)\b[.,!]+\s*", RegexOptions.IgnoreCase), "Very good, Sir. "),
        (new(@"^will do\b[.,!]+\s*", RegexOptions.IgnoreCase), "Right away, Sir. "),
        (new(@"^(done|noted|understood|certainly)\b[.,!]+\s*", RegexOptions.IgnoreCase), "$1, Sir. "),
        (new(@"^(yes|no)\b[.,!]+\s*", RegexOptions.IgnoreCase), "$1, Sir. "),
        (new(@"^(?:sorry|i'm sorry)\b[.,!]+\s*", RegexOptions.IgnoreCase), "My apologies, Sir. "),
    ];

    private static readonly HashSet<string> CommonStarters = new(StringComparer.Ordinal)
    {
        "You", "Your", "There", "The", "This", "That", "These", "Those", "It", "It's", "Its", "We", "Nothing", "Everything",
        "A", "An", "One", "Two", "Three", "Four", "Five", "Just", "Quick", "All", "Something", "Someone", "Nobody", "Today",
        "Tomorrow", "Heads", "Time", "Looks", "Still", "Another", "Several", "Most", "Some", "Well", "Good", "Thanks", "Thank",
    };

    /// <summary>
    /// Puts a spoken line into Guardian's formal manner. Openers ("Done.", "Okay.", "Morning.") become formal with
    /// "Sir"; a closing offer ("Shall I…?") takes "Sir" before the question mark; otherwise an unprompted line opens
    /// with "Sir, …". Lines that already say "Sir" are left alone.
    /// </summary>
    public static string Apply(string? text, bool unprompted)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return text ?? "";
        var s = DropName(text.Trim());
        if (SirWord().IsMatch(s)) return s;

        foreach (var (pattern, replacement) in Openers)
            if (pattern.IsMatch(s))
            {
                s = pattern.Replace(s, replacement, 1);
                s = Regex.Replace(s, @"\bSir\. ([a-z])", m => "Sir. " + char.ToUpperInvariant(m.Groups[1].Value[0]));
                return s.TrimEnd();
            }

        var offer = ClosingOffer().Match(s);
        if (offer.Success) return s[..offer.Groups["q"].Index] + ", Sir?";

        if (!unprompted) return s;
        var firstWord = Regex.Match(s, @"^[\p{L}']+").Value;
        var rest = firstWord.Length > 0 && CommonStarters.Contains(firstWord) && firstWord != "I"
            ? char.ToLowerInvariant(s[0]) + s[1..]
            : s;
        return "Sir, " + rest;
    }

    // "Good evening, the user." / "Morning the user." / "Done, the user." / "the user, the backup failed." → name removed.
    private static string DropName(string s)
    {
        if (string.IsNullOrWhiteSpace(UserFirstName)) return s;
        var name = Regex.Escape(UserFirstName.Trim());
        s = Regex.Replace(s, $@"^((?:good\s+)?(?:morning|afternoon|evening|hello|hi))\s+{name}\b", "$1", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, $@",\s*{name}\b(?=\s*[.,!?;:]|\s*$)", "");
        var opening = Regex.Match(s, $@"^{name},\s*");
        if (opening.Success && s.Length > opening.Length)
            s = char.ToUpperInvariant(s[opening.Length]) + s[(opening.Length + 1)..];
        return s;
    }

    [GeneratedRegex(@"\bSir\b")]
    private static partial Regex SirWord();

    // "…? " at the very end of an offer or question to him: "Shall I…", "Would you like…", "Do you want…", "Want me to…".
    [GeneratedRegex(@"(?:^|[.!?]\s+)(?:Shall I|Should I|Would you like|Do you want|Want me to|Can I)\b[^?]*(?<q>\?)\s*$")]
    private static partial Regex ClosingOffer();
}
