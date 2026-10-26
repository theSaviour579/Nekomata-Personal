using System.Text.RegularExpressions;

namespace Nekomata.AI.Privacy;

/// <summary>A person whose name (and address) must not reach the AI provider.</summary>
public sealed record KnownPerson(string Name, string? Email = null);

/// <summary>
/// Who and what counts as restricted for the AI-use policy: other people by name and address, and organisations. Filled
/// on the desktop from the user's direct reports and mail contacts. Exempt values (the user's own name and mailbox) are
/// never replaced.
/// </summary>
public static class PrivacyDirectory
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, KnownPerson> People = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Organisations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ExemptValues = new(StringComparer.OrdinalIgnoreCase);
    private static volatile DirectoryIndex? s_index;

    private static readonly Regex Bracketed = new(@"\s*[\(\[<].*?[\)\]>]\s*", RegexOptions.Compiled);
    private static readonly Regex NameShape = new(@"^[\p{Lu}][\p{L}'’\-\.]*(?:\s+[\p{L}][\p{L}'’\-\.]*){1,3}$", RegexOptions.Compiled);

    public static int Count { get { lock (Gate) return People.Count; } }

    public static void AddPerson(string? name, string? email = null)
    {
        var clean = CleanName(name);
        if (clean is null) return;
        lock (Gate)
        {
            if (People.TryGetValue(clean, out var existing) && (existing.Email is not null || email is null)) return;
            People[clean] = new KnownPerson(clean, string.IsNullOrWhiteSpace(email) ? existing?.Email : email.Trim().ToLowerInvariant());
            s_index = null;
        }
    }

    public static void AddPeople(IEnumerable<KnownPerson> people)
    {
        foreach (var p in people) AddPerson(p.Name, p.Email);
    }

    public static void AddOrganisation(string? name)
    {
        var clean = name?.Trim() ?? "";
        if (clean.Length < 3 || clean.Length > 80) return;
        lock (Gate)
        {
            if (Organisations.Add(clean)) s_index = null;
        }
    }

    public static void AddOrganisations(IEnumerable<string?> names)
    {
        foreach (var n in names) AddOrganisation(n);
    }

    /// <summary>Values never replaced: the user's own name and address, the company's own name.</summary>
    public static void Exempt(params string?[] values)
    {
        lock (Gate)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) ExemptValues.Add(v.Trim());
            s_index = null;
        }
    }

    public static (IReadOnlyList<KnownPerson> People, IReadOnlyList<string> Organisations, IReadOnlyList<string> Exempt) Snapshot()
    {
        lock (Gate) return (People.Values.ToList(), Organisations.ToList(), ExemptValues.ToList());
    }

    /// <summary>Replaces the whole list (the web host loading the desktop's copy).</summary>
    public static void Load(IEnumerable<KnownPerson> people, IEnumerable<string> organisations, IEnumerable<string> exempt)
    {
        lock (Gate)
        {
            People.Clear(); Organisations.Clear(); ExemptValues.Clear();
            s_index = null;
        }
        AddPeople(people);
        AddOrganisations(organisations);
        Exempt(exempt.ToArray());
    }

    public static void Clear()
    {
        lock (Gate)
        {
            People.Clear(); Organisations.Clear(); ExemptValues.Clear();
            s_index = null;
        }
    }

    internal static DirectoryIndex Index
    {
        get
        {
            var index = s_index;
            if (index is not null) return index;
            lock (Gate)
            {
                index = s_index ??= new DirectoryIndex(People.Values.ToList(), Organisations.ToList(), ExemptValues);
                return index;
            }
        }
    }

    /// <summary>"Smith, Jane (Sales)" → "Jane Smith"; null for anything that isn't a two-to-four word personal name.</summary>
    internal static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Bracketed.Replace(name, " ").Trim();
        var bar = n.IndexOfAny(['|', '/']);
        if (bar > 0) n = n[..bar].Trim();
        var comma = n.IndexOf(',');
        if (comma > 0 && n.IndexOf(',', comma + 1) < 0)
            n = $"{n[(comma + 1)..].Trim()} {n[..comma].Trim()}";
        n = Regex.Replace(n, @"\s+", " ");
        if (n.Length < 5 || n.Length > 60 || n.Any(char.IsDigit) || n.Contains('@')) return null;
        return NameShape.IsMatch(n) ? n : null;
    }
}

internal sealed class DirectoryIndex
{
    public IReadOnlyList<KnownPerson> PeopleLongestFirst { get; }
    public IReadOnlyList<string> OrganisationsLongestFirst { get; }
    /// <summary>First name → the full names that carry it.</summary>
    public IReadOnlyDictionary<string, List<string>> FirstNames { get; }
    public IReadOnlySet<string> Exempt { get; }
    public IReadOnlySet<string> KnownEmails { get; }

    public DirectoryIndex(List<KnownPerson> people, List<string> organisations, IEnumerable<string> exempt)
    {
        Exempt = new HashSet<string>(exempt, StringComparer.OrdinalIgnoreCase);
        PeopleLongestFirst = people.Where(p => !Exempt.Contains(p.Name)).OrderByDescending(p => p.Name.Length).ToList();
        OrganisationsLongestFirst = organisations.Where(o => !Exempt.Contains(o)).OrderByDescending(o => o.Length).ToList();
        var firsts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var p in PeopleLongestFirst)
        {
            var first = p.Name.Split(' ')[0];
            if (first.Length < 3 || Exempt.Contains(first) || CommonNames.AmbiguousWords.Contains(first)) continue;
            if (!firsts.TryGetValue(first, out var list)) firsts[first] = list = [];
            list.Add(p.Name);
        }
        FirstNames = firsts;
        KnownEmails = new HashSet<string>(people.Where(p => p.Email is not null).Select(p => p.Email!), StringComparer.OrdinalIgnoreCase);
    }
}
