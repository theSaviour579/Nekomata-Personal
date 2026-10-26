using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>Small deterministic helpers for the spoken inbox and "what did I promise today" features.</summary>
public static partial class InboxTriageText
{
    /// <summary>Keeps only what the author wrote in this message, dropping the quoted thread and signature-style tails.</summary>
    public static string StripQuoted(string? body)
    {
        var text = (body ?? "").ReplaceLineEndings("\n");
        var cut = QuoteStartRegex().Match(text);
        if (cut.Success) text = text[..cut.Index];
        return text.Trim();
    }

    /// <summary>True for automated mail that a person would not want read aloud as "important".</summary>
    public static bool IsLikelyNoise(string? senderAddress, string? senderName, string? subject)
    {
        var address = (senderAddress ?? "").ToLowerInvariant();
        var local = address.Split('@')[0];
        if (NoiseLocalRegex().IsMatch(local)) return true;
        if (NoiseNameRegex().IsMatch(senderName ?? "")) return true;
        return NoiseSubjectRegex().IsMatch(subject ?? "");
    }

    [GeneratedRegex(@"(?m)^(?:-{2,}\s*Original Message\s*-{2,}|_{8,}|From:\s.+$|On .{5,120} wrote:\s*$|Sent from my )", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteStartRegex();

    [GeneratedRegex(@"^(?:no-?reply|do-?not-?reply|donotreply|notifications?|alerts?|mailer-daemon|postmaster|newsletter|news|marketing|info|updates?|digest|bounce|system|automated?)(?:[._+-].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseLocalRegex();

    [GeneratedRegex(@"\b(?:no-?reply|notifications?|newsletter|mailer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseNameRegex();

    [GeneratedRegex(@"^(?:automatic reply|auto-?reply|out of office|undeliverable|delivery status notification|accepted:|declined:|tentative:|read:)|\bunsubscribe\b|\bnewsletter\b|\bwebinar\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseSubjectRegex();
}
