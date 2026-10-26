namespace Nekomata.Core.Analytics;

public sealed record RoleBandTarget(string Key, string Name, double MinimumPercent, double MaximumPercent);

public sealed record RoleAlignmentBand(
    string Key, string Name, int Minutes, double ActualPercent,
    double MinimumPercent, double MaximumPercent, string Status, string Guidance)
{
    public string TargetLabel => MaximumPercent >= 100
        ? $">= {MinimumPercent:0}%"
        : MinimumPercent <= 0 ? $"< {Math.Ceiling(MaximumPercent):0}%" : $"{MinimumPercent:0}–{MaximumPercent:0}%";
}

public sealed record RoleAlignmentSummary(
    string RoleName, int Score, int RecordedMinutes,
    IReadOnlyList<RoleAlignmentBand> Bands, IReadOnlyList<string> Insights,
    IReadOnlyList<string> Trends)
{
    public const int MinimumEvidenceMinutes = 240;
    public const int EstablishedEvidenceMinutes = 480;
    public bool HasEvidence => RecordedMinutes > 0;
    public bool HasSufficientEvidence => RecordedMinutes >= MinimumEvidenceMinutes;
    public bool IsProvisional => HasEvidence && RecordedMinutes < EstablishedEvidenceMinutes;
    public int EvidenceConfidencePercent => Math.Clamp(
        (int)Math.Round(RecordedMinutes * 100d / EstablishedEvidenceMinutes), 0, 100);
    public string EvidenceMaturityLabel => !HasEvidence ? "Awaiting evidence" :
        RecordedMinutes < MinimumEvidenceMinutes ? "Early signal" :
        RecordedMinutes < EstablishedEvidenceMinutes ? "Provisional" : "Established";
    public string EvidenceMaturityDetail => !HasEvidence
        ? "No completed WorkEpisodes have been recorded this week."
        : HasSufficientEvidence
            ? $"{RecordedMinutes} recorded minutes · {EvidenceMaturityLabel.ToLowerInvariant()} evidence."
            : $"{RecordedMinutes} recorded minutes · {MinimumEvidenceMinutes - RecordedMinutes} more required before drawing role conclusions.";
    public string ScoreLabel => !HasEvidence ? "—" : IsProvisional ? $"{Score}% · PROVISIONAL" : $"{Score}%";
    public IReadOnlySet<string> PersistentUnderweightKeys { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> PersistentOverweightKeys { get; init; } = new HashSet<string>();
}

public sealed class RoleAlignmentSettings
{
    public string RoleName { get; init; } = "Your role";
    public IReadOnlyList<RoleBandTarget> Targets { get; init; } =
    [
        new("strategic", "Strategic / improvement", 25, 40),
        new("leadership", "Leadership / collaboration", 15, 25),
        new("oversight", "Planning & oversight", 15, 25),
        new("technical", "Direct delivery", 10, 20),
        new("reactive", "Reactive requests", 0, 9.9),
        new("admin", "Administration", 0, 9.9)
    ];
}

public sealed class RoleAlignmentEngine(ExecutiveValueEngine valueEngine, RoleAlignmentSettings settings)
{
    public async Task<RoleAlignmentSummary> BuildCurrentWeekAsync(DateTime now)
    {
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var current = await valueEngine.BuildAsync(monday, now);
        var history = new List<ExecutiveValueSummary>();
        for (var weeksAgo = 3; weeksAgo >= 1; weeksAgo--)
        {
            var start = monday.AddDays(-7 * weeksAgo);
            history.Add(await valueEngine.BuildAsync(start, start.AddDays(7)));
        }
        return Analyse(current, settings, history);
    }

    public async Task<RoleAlignmentSummary> BuildCurrentWeekSnapshotAsync(DateTime now)
    {
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        return Analyse(await valueEngine.BuildAsync(monday, now), settings);
    }

    public async Task<IReadOnlyList<RoleAlignmentSummary>> BuildCompletedWeekHistoryAsync(DateTime now, int weeks = 6)
    {
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var result = new List<RoleAlignmentSummary>();
        for (var weeksAgo = Math.Clamp(weeks, 1, 12); weeksAgo >= 1; weeksAgo--)
        {
            var start = monday.AddDays(-7 * weeksAgo);
            result.Add(Analyse(await valueEngine.BuildAsync(start, start.AddDays(7)), settings));
        }
        return result;
    }

    public static RoleAlignmentSummary Analyse(
        ExecutiveValueSummary current,
        RoleAlignmentSettings? settings = null,
        IReadOnlyList<ExecutiveValueSummary>? priorWeeks = null)
    {
        settings ??= new();
        var grouped = current.Episodes.GroupBy(RoleBandKey)
            .ToDictionary(group => group.Key, group => group.Sum(episode => episode.ActiveMinutes));
        var total = current.TotalMinutes;
        var bands = settings.Targets.Select(target =>
        {
            var minutes = grouped.GetValueOrDefault(target.Key);
            var actual = total == 0 ? 0 : Math.Round(minutes * 100d / total, 1);
            var status = total == 0 ? "Awaiting evidence" : total < RoleAlignmentSummary.MinimumEvidenceMinutes ? "Early signal" : actual < target.MinimumPercent ? "Below target" :
                actual > target.MaximumPercent ? "Above target" : "Within range";
            var guidance = status switch
            {
                "Below target" => $"{target.MinimumPercent - actual:0.#} percentage points below the role band.",
                "Above target" => $"{actual - target.MaximumPercent:0.#} percentage points above the role band.",
                "Within range" => "Operating within the intended role band.",
                "Early signal" => $"Only {total} recorded minutes are available; this distribution is not yet a planning conclusion.",
                _ => "Record completed or deferred missions to establish this band."
            };
            return new RoleAlignmentBand(target.Key, target.Name, minutes, actual,
                target.MinimumPercent, target.MaximumPercent, status, guidance);
        }).ToList();

        var deviation = bands.Sum(band => band.ActualPercent < band.MinimumPercent
            ? band.MinimumPercent - band.ActualPercent
            : band.ActualPercent > band.MaximumPercent ? band.ActualPercent - band.MaximumPercent : 0);
        var score = total == 0 ? 0 : Math.Clamp((int)Math.Round(100 - deviation), 0, 100);
        var insights = bands.Where(band => band.Status != "Within range" && band.Status != "Awaiting evidence" && band.Status != "Early signal")
            .OrderByDescending(band => band.ActualPercent > band.MaximumPercent
                ? band.ActualPercent - band.MaximumPercent : band.MinimumPercent - band.ActualPercent)
            .Select(band => $"{band.Name} is {band.Guidance.ToLowerInvariant()}").ToList();
        if (total > 0 && total < RoleAlignmentSummary.MinimumEvidenceMinutes)
            insights.Add($"Role alignment is an early signal only: {total} recorded minutes are below the {RoleAlignmentSummary.MinimumEvidenceMinutes}-minute evidence threshold.");
        else if (total > 0 && insights.Count == 0) insights.Add("Recorded work is within every configured role band this week.");

        var history = priorWeeks ?? [];
        var trends = BuildTrends(history, settings);
        return new(settings.RoleName, score, total, bands, insights, trends)
        {
            PersistentUnderweightKeys = PersistentKeys(history, settings, below: true),
            PersistentOverweightKeys = PersistentKeys(history, settings, below: false)
        };
    }

    public static string RoleBandKey(WorkEpisode episode)
    {
        if (episode.Category == "Team management") return "leadership";
        if (episode.Category == "Administration & communication" || episode.OperatingMode is "Administrative overhead" or "Rework / avoidable") return "admin";
        if (episode.Category == "Operational resilience") return "oversight";
        if (episode.Category == "Operational support" || episode.OperatingMode == "Reactive work") return "reactive";
        if (episode.Category == "Strategic projects" || episode.OperatingMode == "High-leverage work") return "strategic";
        return "technical";
    }

    private static IReadOnlyList<string> BuildTrends(IReadOnlyList<ExecutiveValueSummary> weeks, RoleAlignmentSettings settings)
    {
        if (weeks.Count < 3 || weeks.TakeLast(3).Any(week => week.TotalMinutes < RoleAlignmentSummary.MinimumEvidenceMinutes)) return [];
        var recent = weeks.TakeLast(3).ToList();
        var results = new List<string>();
        foreach (var target in settings.Targets)
        {
            var values = recent.Select(week => Percentage(week, target.Key)).ToList();
            if (values[0] < values[1] && values[1] < values[2] && values[2] - values[0] >= 2)
                results.Add($"{target.Name} increased for three consecutive recorded weeks ({values[0]:0.#}% → {values[2]:0.#}%).");
            else if (values[0] > values[1] && values[1] > values[2] && values[0] - values[2] >= 2)
                results.Add($"{target.Name} decreased for three consecutive recorded weeks ({values[0]:0.#}% → {values[2]:0.#}%).");
        }
        return results;
    }

    private static double Percentage(ExecutiveValueSummary week, string key)
    {
        var minutes = week.Episodes.Where(episode => RoleBandKey(episode) == key).Sum(episode => episode.ActiveMinutes);
        return week.TotalMinutes == 0 ? 0 : minutes * 100d / week.TotalMinutes;
    }

    private static IReadOnlySet<string> PersistentKeys(
        IReadOnlyList<ExecutiveValueSummary> weeks, RoleAlignmentSettings settings, bool below)
    {
        if (weeks.Count < 3 || weeks.TakeLast(3).Any(week => week.TotalMinutes < RoleAlignmentSummary.MinimumEvidenceMinutes)) return new HashSet<string>();
        var recent = weeks.TakeLast(3).ToList();
        return settings.Targets.Where(target => recent.All(week => below
                ? Percentage(week, target.Key) < target.MinimumPercent
                : Percentage(week, target.Key) > target.MaximumPercent))
            .Select(target => target.Key).ToHashSet();
    }
}
