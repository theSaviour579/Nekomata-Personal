namespace Nekomata.Core.Guardian.Outcomes;

public enum ActivityEvidenceKind
{
    Work,
    Calendar,
    Interruption,
    NonWork
}

public sealed record ActivityEvidence(
    string SourceKey,
    string SourceType,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    ActivityEvidenceKind Kind,
    int ConfidencePercent = 100,
    bool IsNekomataManaged = false);

public sealed class ReconciledActivity
{
    public string Title { get; init; } = "";
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public ActivityEvidenceKind Kind { get; init; }
    public IReadOnlyList<string> Sources { get; init; } = [];
    public int ConfidencePercent { get; init; }
    public int Minutes => Math.Max(0, (int)Math.Round((End - Start).TotalMinutes));
    public string TimeLabel => $"{Start.LocalDateTime:HH:mm}–{End.LocalDateTime:HH:mm}";
    public string EvidenceLabel => $"{string.Join(" + ", Sources)} · {ConfidencePercent}% evidence";
}

public sealed class ActivityReconciliationGap
{
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public string ReviewTitle { get; set; } = "";
    public string ReviewCategory { get; set; } = "Unplanned work";
    public int Minutes => Math.Max(0, (int)Math.Round((End - Start).TotalMinutes));
    public string TimeLabel => $"{Start.LocalDateTime:HH:mm}–{End.LocalDateTime:HH:mm}";
    public string Prompt => $"{Minutes} minutes have no supporting activity evidence.";
}

public sealed class DailyActivityReconciliationResult
{
    public IReadOnlyList<ReconciledActivity> Activities { get; init; } = [];
    public IReadOnlyList<ActivityReconciliationGap> Gaps { get; init; } = [];
    public int WorkableMinutes { get; init; }
    public int ExplainedMinutes { get; init; }
    public int RecordedWorkMinutes { get; init; }
    public int InterruptionMinutes { get; init; }
    public int MergedDuplicateCount { get; init; }
    public int CoveragePercent => WorkableMinutes == 0 ? 100 : Math.Clamp((int)Math.Round(ExplainedMinutes * 100d / WorkableMinutes), 0, 100);
    public bool HasGaps => Gaps.Count > 0;
    public string CoverageLabel => $"{CoveragePercent}% of the elapsed workday explained";
    public string Summary => WorkableMinutes == 0
        ? "The working day has not started yet."
        : $"{Format(RecordedWorkMinutes)} recorded work · {Format(InterruptionMinutes)} interruptions · {Gaps.Count} unexplained gap{(Gaps.Count == 1 ? "" : "s")}.";
    public string EvidenceDetail => MergedDuplicateCount == 0
        ? "Observed calendar, mission, call and confirmed non-work evidence remain distinct."
        : $"{MergedDuplicateCount} matching calendar/mission record{(MergedDuplicateCount == 1 ? " was" : "s were")} merged to prevent double-counting.";

    private static string Format(int minutes) => $"{minutes / 60}h {minutes % 60:00}m";
}

public sealed class DailyActivityReconciliationEngine
{
    public DailyActivityReconciliationResult Reconcile(
        DateTimeOffset workdayStart,
        DateTimeOffset workdayEnd,
        DateTimeOffset asOf,
        DateTimeOffset? lunchStart,
        DateTimeOffset? lunchEnd,
        IEnumerable<ActivityEvidence> evidence,
        int minimumGapMinutes = 15)
    {
        var effectiveEnd = asOf < workdayEnd ? asOf : workdayEnd;
        if (effectiveEnd <= workdayStart)
            return new DailyActivityReconciliationResult();

        var workingWindows = SplitForLunch(workdayStart, effectiveEnd, lunchStart, lunchEnd).ToList();
        var clipped = evidence
            .Where(x => x.End > x.Start)
            .SelectMany(item => workingWindows.Select(window => Clip(item, window.Start, window.End)))
            .Where(x => x is not null)
            .Cast<ActivityEvidence>()
            .OrderBy(x => x.Start)
            .ThenBy(x => x.End)
            .ToList();

        var duplicateCalendarKeys = DuplicateCalendarKeys(clipped);
        var mergedDuplicateCount = duplicateCalendarKeys.Count;
        var activities = clipped.Where(x => !duplicateCalendarKeys.Contains(x.SourceKey)).Select(x => new ReconciledActivity
        {
            Title = x.Title,
            Start = x.Start,
            End = x.End,
            Kind = x.Kind,
            Sources = [x.SourceType],
            ConfidencePercent = Math.Clamp(x.ConfidencePercent, 0, 100)
        }).ToList();

        var covered = Union(clipped.Select(x => (x.Start, x.End))).ToList();
        var productive = Union(clipped.Where(x => x.Kind is ActivityEvidenceKind.Work or ActivityEvidenceKind.Calendar)
            .Select(x => (x.Start, x.End))).ToList();
        var interruptions = Union(clipped.Where(x => x.Kind == ActivityEvidenceKind.Interruption)
            .Select(x => (x.Start, x.End))).ToList();
        var gaps = BuildGaps(workingWindows, covered, Math.Max(5, minimumGapMinutes));

        return new DailyActivityReconciliationResult
        {
            Activities = activities,
            Gaps = gaps,
            WorkableMinutes = Minutes(workingWindows),
            ExplainedMinutes = Minutes(covered),
            RecordedWorkMinutes = Minutes(productive),
            InterruptionMinutes = Minutes(interruptions),
            MergedDuplicateCount = mergedDuplicateCount
        };
    }

    private static ActivityEvidence? Clip(ActivityEvidence item, DateTimeOffset start, DateTimeOffset end)
    {
        var clippedStart = item.Start > start ? item.Start : start;
        var clippedEnd = item.End < end ? item.End : end;
        return clippedEnd <= clippedStart ? null : item with { Start = clippedStart, End = clippedEnd };
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitForLunch(
        DateTimeOffset start, DateTimeOffset end, DateTimeOffset? lunchStart, DateTimeOffset? lunchEnd)
    {
        if (lunchStart is null || lunchEnd is null || lunchEnd <= start || lunchStart >= end)
        {
            yield return (start, end);
            yield break;
        }
        if (lunchStart > start) yield return (start, lunchStart.Value < end ? lunchStart.Value : end);
        if (lunchEnd < end) yield return (lunchEnd.Value > start ? lunchEnd.Value : start, end);
    }

    private static List<ActivityReconciliationGap> BuildGaps(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> workingWindows,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> covered,
        int minimumMinutes)
    {
        var result = new List<ActivityReconciliationGap>();
        foreach (var window in workingWindows)
        {
            var cursor = window.Start;
            foreach (var item in covered.Where(x => x.End > window.Start && x.Start < window.End))
            {
                var start = item.Start > window.Start ? item.Start : window.Start;
                if ((start - cursor).TotalMinutes >= minimumMinutes)
                    result.Add(new ActivityReconciliationGap { Start = cursor, End = start });
                if (item.End > cursor) cursor = item.End < window.End ? item.End : window.End;
            }
            if ((window.End - cursor).TotalMinutes >= minimumMinutes)
                result.Add(new ActivityReconciliationGap { Start = cursor, End = window.End });
        }
        return result;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Union(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> source)
    {
        var ordered = source.OrderBy(x => x.Start).ThenBy(x => x.End).ToList();
        if (ordered.Count == 0) yield break;
        var current = ordered[0];
        foreach (var item in ordered.Skip(1))
        {
            if (item.Start <= current.End)
                current = (current.Start, item.End > current.End ? item.End : current.End);
            else
            {
                yield return current;
                current = item;
            }
        }
        yield return current;
    }

    private static HashSet<string> DuplicateCalendarKeys(IReadOnlyList<ActivityEvidence> items)
    {
        var missions = items.Where(x => x.Kind == ActivityEvidenceKind.Work).ToList();
        return items.Where(calendar => calendar.Kind == ActivityEvidenceKind.Calendar && missions.Any(mission =>
                calendar.Start < mission.End && calendar.End > mission.Start &&
                (calendar.IsNekomataManaged || SimilarTitle(calendar.Title, mission.Title))))
            .Select(x => x.SourceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool SimilarTitle(string left, string right)
    {
        var a = Tokens(left);
        var b = Tokens(right);
        if (a.Count == 0 || b.Count == 0) return false;
        var shared = a.Intersect(b).Count();
        return shared >= 2 && shared >= Math.Min(a.Count, b.Count) / 2d;
    }

    private static HashSet<string> Tokens(string value) => value.ToLowerInvariant()
        .Replace("focus", " ").Replace("completed", " ").Replace("work", " ")
        .Split([' ', '·', '-', ':', '–', '—'], StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length >= 3)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int Minutes(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals) =>
        Math.Max(0, (int)Math.Round(intervals.Sum(x => (x.End - x.Start).TotalMinutes)));
}
