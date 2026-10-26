using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

public enum WatchKind { Reply, TeamsMessage }

/// <summary>Something the user asked Guardian to keep an eye on.</summary>
public sealed class Watch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WatchKind Kind { get; set; }
    /// <summary>A person's name.</summary>
    public string Target { get; set; } = "";
    /// <summary>Optional subject words for a reply ("about the licences").</summary>
    public string About { get; set; } = "";
    public string Said { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? FiredAt { get; set; }
}

public sealed record WatchMail(string SenderName, string SenderAddress, string Subject, DateTimeOffset At);
public sealed record WatchChat(string Sender, string Text, DateTimeOffset At, bool FromMe);
public sealed record WatchEvidence(IReadOnlyList<WatchMail> Mail, IReadOnlyList<WatchChat> Chats);

/// <summary>
/// "Keep an eye on it": the user asks Guardian to watch for something it can already see, and it tells them when it
/// happens: "tell me when Sam replies (about the budget)" · "let me know when Kev messages me".
/// Watching only reads; it never acts. Pure and tested: the desktop gathers the evidence every couple of minutes.
/// </summary>
public static partial class GuardianWatches
{
    public static Watch? Parse(string text, DateTimeOffset now)
    {
        var s = text.Trim().TrimEnd('.', '!', '?').Replace('’', '\'');
        var (body, expires) = Expiry(s, now);

        if (TeamsRegex().Match(body) is { Success: true } m)
            return New(WatchKind.TeamsMessage, Clean(m.Groups["who"].Value), "", s, now, expires);
        if (ReplyRegex().Match(body) is { Success: true } r)
            return New(WatchKind.Reply, Clean(r.Groups["who"].Value), r.Groups["about"].Value.Trim(), s, now, expires);
        return null;
    }

    /// <summary>What to tell the user if the watch has been met, or null.</summary>
    public static string? Check(Watch w, WatchEvidence e)
    {
        switch (w.Kind)
        {
            case WatchKind.Reply:
                var mail = e.Mail.Where(x => x.At > w.CreatedAt && IsPerson(w.Target, x.SenderName, x.SenderAddress) &&
                                             (w.About.Length == 0 || Words(w.About).Any(a => x.Subject.Contains(a, StringComparison.OrdinalIgnoreCase))))
                    .OrderBy(x => x.At).FirstOrDefault();
                return mail is null ? null : $"{FirstWord(mail.SenderName, w.Target)} has replied: \"{Trim(mail.Subject, 90)}\".";
            case WatchKind.TeamsMessage:
                var chat = e.Chats.Where(x => !x.FromMe && x.At > w.CreatedAt && IsPerson(w.Target, x.Sender, "")).OrderBy(x => x.At).FirstOrDefault();
                return chat is null ? null : $"{FirstWord(chat.Sender, w.Target)} has messaged you on Teams: \"{Trim(chat.Text, 90)}\".";
        }
        return null;
    }

    public static string Describe(Watch w) => w.Kind switch
    {
        WatchKind.TeamsMessage => $"a Teams message from {w.Target}",
        _ => $"a reply from {w.Target}{(w.About.Length > 0 ? $" about {w.About}" : "")}",
    };

    private static Watch New(WatchKind kind, string target, string about, string said, DateTimeOffset now, DateTimeOffset expires) =>
        new() { Kind = kind, Target = target, About = about, Said = said, CreatedAt = now, ExpiresAt = expires };

    // "tonight" → until 10:00 tomorrow; "today" → end of today; "this week" → Friday evening; otherwise a week.
    private static (string Body, DateTimeOffset Expires) Expiry(string s, DateTimeOffset now)
    {
        var local = now.LocalDateTime;
        var m = Regex.Match(s, @"\s+(?<w>tonight|overnight|today|this afternoon|this morning|this week)$", RegexOptions.IgnoreCase);
        if (!m.Success) return (s, now.AddDays(7));
        var body = s[..m.Index];
        DateTime until = m.Groups["w"].Value.ToLowerInvariant() switch
        {
            "tonight" or "overnight" => local.Date.AddDays(1).AddHours(10),
            "this week" => local.Date.AddDays(Math.Max(0, ((int)DayOfWeek.Friday - (int)local.DayOfWeek + 7) % 7)).AddHours(18),
            _ => local.Date.AddDays(1).AddMinutes(-1),
        };
        return (body, new DateTimeOffset(until, TimeZoneInfo.Local.GetUtcOffset(until)));
    }

    private static bool IsPerson(string target, string name, string address)
    {
        var want = Words(target);
        if (want.Count == 0) return false;
        var have = Words(name);
        return want.All(w => have.Contains(w, StringComparer.OrdinalIgnoreCase)) ||
               (address.Length > 0 && want.All(w => address.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> Words(string s) =>
        Regex.Matches(s, @"[\p{L}\d']{2,}").Select(m => m.Value).Where(w => !Stop.Contains(w)).ToList();

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase) { "the", "my", "our", "a", "an", "job", "about", "re", "on" };

    private static string Clean(string s) => Regex.Replace(s.Trim(), @"^(?:the|my|our)\s+", "", RegexOptions.IgnoreCase).Trim();
    private static string FirstWord(string name, string fallback) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fallback;
    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    private const string Ask = @"^(?:please\s+)?(?:tell me|let me know|ping me|alert me|give me a shout|shout|warn me|keep an eye out and tell me)\s+(?:when|if|as soon as|once)\s+";

    [GeneratedRegex(Ask + @"(?<who>[\p{L}' .-]+?)\s+(?:messages|pings|teams)\s+me(?:\s+on teams)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TeamsRegex();

    [GeneratedRegex(Ask + @"(?<who>[\p{L}' .-]+?)\s+(?:replies|responds|gets back to me|gets back|emails me|writes back|answers|comes back to me)(?:\s+(?:about|on|re|regarding)\s+(?<about>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyRegex();
}
