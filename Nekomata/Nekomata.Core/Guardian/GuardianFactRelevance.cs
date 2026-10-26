using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian;

/// <summary>
/// Picks which of the user's remembered facts ("remember that Sarah prefers calls") matter for a meeting:
/// facts that name an attendee (first name, surname or address part) or share a distinctive word with the subject.
/// Deterministic, so the spoken brief only ever hears facts the user actually stated.
/// </summary>
public static partial class GuardianFactRelevance
{
    private static readonly HashSet<string> CommonIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "meeting", "call", "catch", "sync", "weekly", "daily", "monthly", "review", "with", "and", "the",
        "for", "teams", "update", "chat", "check", "about", "from", "that", "this", "they", "their", "will", "have"
    };

    public static IReadOnlyList<string> ForMeeting(
        IEnumerable<string> facts, string? subject, IEnumerable<string> attendees, int max = 4,
        string? userDisplayName = null)
    {
        var ignored = new HashSet<string>(CommonIgnored, StringComparer.OrdinalIgnoreCase);
        foreach (Match part in WordRegex().Matches(userDisplayName ?? ""))
            if (part.Value.Length >= 3) ignored.Add(part.Value);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attendee in attendees)
        {
            var value = attendee.Trim();
            var parts = value.Contains('@')
                ? value.Split('@')[0].Split('.', '_', '-')
                : WordRegex().Matches(value).Select(m => m.Value).ToArray();
            foreach (var part in parts)
                if (part.Length >= 3 && !ignored.Contains(part)) names.Add(part);
        }

        var subjectWords = WordRegex().Matches(subject ?? "")
            .Select(m => m.Value)
            .Where(w => w.Length >= 4 && !ignored.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return facts
            .Select(fact => (fact, words: WordRegex().Matches(fact).Select(m => m.Value.TrimEnd('\'', 's')).ToList(),
                              raw: WordRegex().Matches(fact).Select(m => m.Value).ToList()))
            .Select(x => (x.fact, score:
                (x.raw.Concat(x.words).Any(names.Contains) ? 2 : 0) +
                (x.raw.Any(subjectWords.Contains) ? 1 : 0)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.fact)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }

    [GeneratedRegex(@"[\p{L}\p{N}']+")] private static partial Regex WordRegex();
}
