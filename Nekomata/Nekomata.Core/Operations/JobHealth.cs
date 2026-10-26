namespace Nekomata.Core.Operations;

/// <summary>The last run of one of Guardian's background jobs.</summary>
public sealed class JobRecord
{
    public DateTimeOffset? LastOk { get; set; }
    public DateTimeOffset? LastRun { get; set; }
    public bool LastFailed { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>How often a background job should succeed. <paramref name="Grace"/> is the slack before it counts as stopped.</summary>
public sealed record JobExpectation(string Job, string Name, TimeSpan Every, TimeSpan Grace, string Feature, string Fix);

public sealed record JobProblem(JobExpectation Job, string Problem);

/// <summary>
/// Guardian checking itself: a background job that failed on its last run, or that hasn't succeeded for longer than it
/// should (measured from when Nekomata was started, so a closed laptop over the weekend isn't a problem, and with
/// weekends added to daily jobs' slack). Pure, so it's tested.
/// </summary>
public static class JobHealth
{
    public static IReadOnlyList<JobProblem> Problems(IEnumerable<JobExpectation> expected, IReadOnlyDictionary<string, JobRecord> records,
        DateTimeOffset now, DateTimeOffset runningSince, Func<string, bool> featureOn)
    {
        var problems = new List<JobProblem>();
        foreach (var job in expected)
        {
            if (job.Feature.Length > 0 && !featureOn(job.Feature)) continue;
            records.TryGetValue(job.Job, out var r);
            if (r is { LastFailed: true, LastRun: { } failedAt } && (r.LastOk is null || failedAt > r.LastOk))
            {
                problems.Add(new(job, $"failed {Ago(failedAt, now)}: {r.Detail}"));
                continue;
            }
            var slack = job.Every + job.Grace;
            if (job.Every >= TimeSpan.FromHours(20) && now.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Sunday or DayOfWeek.Saturday) slack += TimeSpan.FromDays(2);
            var since = r?.LastOk is { } ok && ok > runningSince ? ok : runningSince;
            if (now - since > slack)
                problems.Add(new(job, r?.LastOk is { } last ? $"hasn't worked since {last.LocalDateTime:ddd d MMM HH:mm}" : "hasn't worked since Nekomata started"));
        }
        return problems;
    }

    private static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var span = now - at;
        return span.TotalMinutes < 90 ? $"{Math.Max(1, (int)span.TotalMinutes)} min ago" : span.TotalHours < 36 ? $"at {at.LocalDateTime:HH:mm}" : $"on {at.LocalDateTime:ddd d MMM}";
    }
}
