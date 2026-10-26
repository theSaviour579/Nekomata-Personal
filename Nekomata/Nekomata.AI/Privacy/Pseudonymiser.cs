using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nekomata.AI.Privacy;

/// <summary>
/// One AI call's placeholders. Before anything is sent, people, email addresses, phone numbers, customer
/// organisations and money amounts are replaced by placeholders such as [Person 2] or [Amount 1]; the AI works with
/// those, and the answer has the real values put back here. The table never leaves the PC (or the web host).
/// </summary>
public sealed class Pseudonymiser
{
    public const string Instruction =
        "Privacy: some names, email addresses, phone numbers, organisations and money amounts have been replaced with " +
        "placeholders in square brackets, such as [Person 2], [Person 2 first name], [Email 1], [Phone 1], [Organisation 1] " +
        "and [Amount 3]. Each stands for one real value hidden from you. Use the placeholders exactly as written wherever you " +
        "would use the value (for example 'Hi [Person 2 first name],'). Never guess who or what they are, never change their " +
        "wording or numbering, and don't do sums with [Amount] placeholders; just refer to them.";

    private static readonly Regex Token = new(@"\[(Person|Email|Phone|Organisation|Amount) (\d+)( first name)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Email = new(@"(?<![\w.+-])[A-Za-z0-9._%+'\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}(?![\w-])", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"(?<![\w#/.-])(?:\+44\s?\(?0?\)?\s?|\(?0)\d{2,4}\)?(?:[\s-]?\d){6,8}(?![\w/.-])", RegexOptions.Compiled);
    private static readonly Regex Money = new(
        @"(?:[£$€]|\b(?:GBP|USD|EUR)\s?)\s?\d[\d,]*(?:\.\d+)?(?:\s?(?:k|m|bn|million|thousand)\b)?|\b\d[\d,]*(?:\.\d+)?\s?(?:GBP|pounds|quid)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Labelled = new(
        @"(?m)\b(?i:full\s+name|name|employee|new\s+starter|starter|leaver|requester|requested\s+by|reported\s+by|line\s+manager|manager|contact|user)\s*[:\-–]\s*(?<n>\p{Lu}[\p{Ll}'’\-]+(?:[ \t]+\p{Lu}[\p{Ll}'’\-]+){1,2})",
        RegexOptions.Compiled);
    private static readonly Regex Titled = new(@"\b(?:Mr|Mrs|Ms|Miss|Dr)\.?\s+(?<n>\p{Lu}[\p{Ll}'’\-]+(?:\s+\p{Lu}[\p{Ll}'’\-]+)?)", RegexOptions.Compiled);
    private static readonly Regex Pair = new(@"\b(?<f>\p{Lu}[\p{Ll}]+)\s+(?<s>(?:Mc|Mac|O')?\p{Lu}[\p{Ll}'’]+(?:-\p{Lu}[\p{Ll}]+)?)\b", RegexOptions.Compiled);
    private static readonly Regex Greeting = new(
        @"(?m)(?:^|\n)\s*(?:Hi|Hello|Dear|Morning|Afternoon|Hiya|Hey)\s+(?<n>\p{Lu}[\p{Ll}]+)\b|(?:Kind regards|Many thanks|Regards|Thanks|Cheers|Best wishes|Best)\s*,?\s*\r?\n\s*(?<n>\p{Lu}[\p{Ll}]+)\b",
        RegexOptions.Compiled);

    private readonly Dictionary<string, string> _tokenFor = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _valueOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _next = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly DirectoryIndex _index = PrivacyDirectory.Index;

    /// <summary>True once anything has been replaced.</summary>
    public bool Masked => _valueOf.Count > 0;

    /// <summary>How many distinct values of each kind were hidden (for the send log; never the values).</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    public string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = OutsideTokens(text, t => Email.Replace(t, m => _index.Exempt.Contains(m.Value) ? m.Value : TokenFor("Email", m.Value)));
        s = OutsideTokens(s, t => Phone.Replace(t, m => TokenFor("Phone", m.Value.Trim())));
        s = OutsideTokens(s, t => Money.Replace(t, m => TokenFor("Amount", m.Value.Trim())));

        foreach (var org in _index.OrganisationsLongestFirst)
            if (s.Contains(org, StringComparison.OrdinalIgnoreCase))
                s = OutsideTokens(s, t => Regex.Replace(t, $@"(?<![\w]){Regex.Escape(org)}(?![\w])", m => TokenFor("Organisation", org), RegexOptions.IgnoreCase));

        foreach (var person in _index.PeopleLongestFirst)
            if (s.Contains(person.Name, StringComparison.OrdinalIgnoreCase))
                s = OutsideTokens(s, t => Regex.Replace(t, $@"(?<![\w]){NamePattern(person.Name)}(?![\w])", m => TokenFor("Person", person.Name), RegexOptions.IgnoreCase));

        // People not in the directory: "Name: Jane Smith", "Mrs Smith", "Jane Smith" with a common first name.
        s = OutsideTokens(s, t => Labelled.Replace(t, m => _index.Exempt.Contains(m.Groups["n"].Value) ? m.Value
            : Swap(m, m.Groups["n"], TokenFor("Person", m.Groups["n"].Value))));
        s = OutsideTokens(s, t => Titled.Replace(t, m => TokenFor("Person", m.Value)));
        s = OutsideTokens(s, t => Pair.Replace(t, m =>
            CommonNames.FirstNames.Contains(m.Groups["f"].Value) && !CommonNames.NotSurnames.Contains(m.Groups["s"].Value) &&
            !CommonNames.AmbiguousWords.Contains(m.Groups["s"].Value) && !_index.Exempt.Contains(m.Value)
                ? TokenFor("Person", m.Value)
                : m.Value));

        // First names on their own: linked to the full name when that person is already in this call.
        s = OutsideTokens(s, t => Regex.Replace(t, @"\b\p{Lu}[\p{Ll}'’\-]+\b", m => FirstNameToken(m.Value) ?? m.Value));
        s = OutsideTokens(s, t => Greeting.Replace(t, m => Swap(m, m.Groups["n"],
            _index.Exempt.Contains(m.Groups["n"].Value) || CommonNames.NotSurnames.Contains(m.Groups["n"].Value) ||
            CommonNames.AmbiguousWords.Contains(m.Groups["n"].Value) ? m.Groups["n"].Value : TokenFor("Person", m.Groups["n"].Value))));
        return s;
    }

    /// <summary>Puts the real values back. With json, each value is escaped for a JSON string.</summary>
    public string Unmask(string? text, bool json = false)
    {
        if (string.IsNullOrEmpty(text) || _valueOf.Count == 0) return text ?? "";
        return Token.Replace(text, m =>
        {
            var key = $"[{Capital(m.Groups[1].Value)} {m.Groups[2].Value}]";
            if (!_valueOf.TryGetValue(key, out var value)) return m.Value;
            if (m.Groups[3].Success) value = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return json ? JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString() : value;
        });
    }

    /// <summary>True when the text holds anything the policy restricts (used to keep such lines off the cloud voice).</summary>
    public static bool ContainsRestricted(string? text)
    {
        var p = new Pseudonymiser();
        p.Mask(text);
        return p.Masked;
    }

    private string? FirstNameToken(string word)
    {
        if (_index.Exempt.Contains(word) || !_index.FirstNames.TryGetValue(word, out var fullNames)) return null;
        foreach (var full in fullNames)
            if (_tokenFor.TryGetValue(full, out var token)) return token[..^1] + " first name]";
        return TokenFor("Person", word);
    }

    private string TokenFor(string kind, string value)
    {
        if (_tokenFor.TryGetValue(value, out var existing)) return existing;
        var n = _next.TryGetValue(kind, out var c) ? c + 1 : 1;
        _next[kind] = n;
        _counts[kind] = n;
        var token = $"[{kind} {n}]";
        _tokenFor[value] = token;
        _valueOf[token] = value;
        return token;
    }

    private static string NamePattern(string name) =>
        string.Join(@"\s+", name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));

    private static string Swap(Match whole, Group part, string replacement) =>
        whole.Value[..(part.Index - whole.Index)] + replacement + whole.Value[(part.Index - whole.Index + part.Length)..];

    private static string Capital(string kind) => char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant();

    // Applies a pass to the text between placeholders already made, so a placeholder is never rewritten.
    private static string OutsideTokens(string text, Func<string, string> pass)
    {
        var tokens = Token.Matches(text);
        if (tokens.Count == 0) return pass(text);
        var sb = new StringBuilder(text.Length);
        var at = 0;
        foreach (Match t in tokens)
        {
            if (t.Index > at) sb.Append(pass(text[at..t.Index]));
            sb.Append(t.Value);
            at = t.Index + t.Length;
        }
        if (at < text.Length) sb.Append(pass(text[at..]));
        return sb.ToString();
    }
}
