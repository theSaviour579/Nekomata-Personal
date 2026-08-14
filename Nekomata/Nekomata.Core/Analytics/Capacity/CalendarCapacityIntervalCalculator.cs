namespace Nekomata.Core.Analytics.Capacity;

public static class CalendarCapacityIntervalCalculator
{
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> Calculate(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals,
        DateTimeOffset workStart,
        DateTimeOffset workEnd,
        DateTimeOffset? lunchStart = null,
        DateTimeOffset? lunchEnd = null)
    {
        var clipped = intervals
            .Where(interval => interval.End > workStart && interval.Start < workEnd)
            .Select(interval => (
                Start: interval.Start < workStart ? workStart : interval.Start,
                End: interval.End > workEnd ? workEnd : interval.End))
            .Where(interval => interval.End > interval.Start)
            .SelectMany(interval => ExcludeLunch(interval, lunchStart, lunchEnd))
            .OrderBy(interval => interval.Start)
            .ToList();

        if (clipped.Count == 0) return [];
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)> { clipped[0] };
        foreach (var interval in clipped.Skip(1))
        {
            var last = merged[^1];
            if (interval.Start <= last.End)
                merged[^1] = (last.Start, interval.End > last.End ? interval.End : last.End);
            else
                merged.Add(interval);
        }
        return merged;
    }

    public static int TotalMinutes(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> intervals) =>
        intervals.Sum(interval => Math.Max(0, (int)(interval.End - interval.Start).TotalMinutes));

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> ExcludeLunch(
        (DateTimeOffset Start, DateTimeOffset End) interval,
        DateTimeOffset? lunchStart,
        DateTimeOffset? lunchEnd)
    {
        if (lunchStart is null || lunchEnd is null || lunchEnd <= lunchStart ||
            interval.End <= lunchStart || interval.Start >= lunchEnd)
        {
            yield return interval;
            yield break;
        }
        if (interval.Start < lunchStart)
            yield return (interval.Start, lunchStart.Value);
        if (interval.End > lunchEnd)
            yield return (lunchEnd.Value, interval.End);
    }
}