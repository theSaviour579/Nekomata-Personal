using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian.Commands;

public enum GuardianEntityType { Task, Project, Person }

public sealed record GuardianEntity(GuardianEntityType Type, string Id, string Name, object? Value = null);
public sealed record GuardianEntityResolution(GuardianEntity? Entity, int Confidence, bool IsAmbiguous,
    IReadOnlyList<GuardianEntity> Alternatives)
{
    public bool IsResolved => Entity is not null && !IsAmbiguous && Confidence >= 70;
}

public static partial class GuardianEntityResolver
{
    public static GuardianEntityResolution Resolve(string? query, IEnumerable<GuardianEntity> entities,
        GuardianEntityType? preferredType = null)
    {
        var needle = Normalize(query);
        if (needle.Length == 0) return new(null, 0, false, []);
        var scored = entities
            .Where(x => preferredType is null || x.Type == preferredType)
            .Select(x => (Entity: x, Score: Score(needle, Normalize(x.Name), Normalize(x.Id))))
            .Where(x => x.Score >= 45)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Entity.Name.Length)
            .ToList();
        if (scored.Count == 0) return new(null, 0, false, []);
        var ambiguous = scored.Count > 1 && scored[0].Score == scored[1].Score &&
                        !scored[0].Entity.Id.Equals(scored[1].Entity.Id, StringComparison.OrdinalIgnoreCase);
        return new(scored[0].Entity, scored[0].Score, ambiguous, scored.Take(5).Select(x => x.Entity).ToList());
    }

    private static int Score(string query, string name, string id)
    {
        if (query == id || query == name) return 100;
        if (name.StartsWith(query + " ", StringComparison.Ordinal)) return 92;
        if (name.Contains(query, StringComparison.Ordinal)) return 84;
        var queryTokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameTokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = queryTokens.Count(q => nameTokens.Any(n => n == q || n.StartsWith(q, StringComparison.Ordinal)));
        if (matched == queryTokens.Length) return 75 + Math.Min(8, matched * 2);
        return matched == 0 ? 0 : 40 + (int)Math.Round(30d * matched / queryTokens.Length);
    }

    private static string Normalize(string? value) => SpaceRegex().Replace(
        NonWordRegex().Replace(value?.Trim().ToLowerInvariant() ?? "", " "), " ").Trim();
    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.IgnoreCase)] private static partial Regex NonWordRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRegex();
}
