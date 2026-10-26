using System.IO;
using System.Text;
using Nekomata.Core.Operations;

namespace Nekomata.UI.ViewModels;

// Guardian checking itself. Each background job records when it last worked (and the reason when it didn't) in
// job-health.json. Every hour Guardian looks for a job whose last run failed, or that hasn't worked for longer than it
// should, counted from when Nekomata started so a closed laptop isn't a fault. Each goes to Needs you (once a day) with
// what to do, and clears itself when the job recovers; the web Brief shows them too. "Is Guardian OK?" lists every job.
// This sits alongside the morning self-check, which tests the connections themselves.
public partial class MainViewModel
{
    private static string JobHealthPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "job-health.json");
    private readonly DateTimeOffset _jobsRunningSince = DateTimeOffset.Now;
    private Dictionary<string, JobRecord>? _jobRecords;
    private Dictionary<string, JobRecord> JobRecords => _jobRecords ??= LoadJson<Dictionary<string, JobRecord>>(JobHealthPath) ?? new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _jobsSavedAt, _jobsCheckedAt;
    private IReadOnlyList<JobProblem> _jobProblems = [];

    private static readonly JobExpectation[] JobExpectations =
    [
        new("web-sync", "Web sync", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(25), "web-sync", "Check the database connection (Health on the status page)."),
        new("web-digest", "Web briefing", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), "web-sync", "Check the database connection; the web Brief will be out of date meanwhile."),
        new("capture", "Capture from Teams and email", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(50), "capture", "Check Teams and mail still work (say \"self check\")."),
        new("monitoring", "Monitoring email check", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(90), "monitoring", "Check mail still works (say \"self check\")."),
        new("monitoring-mail", "Monitoring emails arriving", TimeSpan.FromDays(1), TimeSpan.FromDays(2), "monitoring", "No backup or NAS emails for three days: check the senders on the settings page, or whether the jobs are still emailing."),
        new("service-health", "Service health", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(50), "service-health", "Say \"service health\" to see the error; the permission may need granting again."),
        new("expiry", "Expiry watch", TimeSpan.FromDays(1), TimeSpan.FromHours(6), "expiry", "Say \"what's expiring\" to see what couldn't be checked."),
        new("state-backup", "Nightly backup of Guardian's data", TimeSpan.FromDays(3), TimeSpan.FromDays(1), "state-backup", "It runs after 17:00 while Nekomata is open. Say \"back up Guardian\" to run it now."),
        new("renewals", "Renewal scan", TimeSpan.FromDays(1), TimeSpan.FromHours(8), "renewals", "Check mail still works (say \"self check\")."),
        new("brief", "Morning briefing", TimeSpan.FromDays(3650), TimeSpan.Zero, "", "Say \"morning brief\" to try it again."),
        new("desk-forecast", "Desk forecast", TimeSpan.FromDays(7), TimeSpan.FromDays(2), "desk-forecast", "Say \"desk forecast\" to see the error."),
        new("starter-audit", "Starter checks", TimeSpan.FromDays(1), TimeSpan.FromDays(1), "starter-audit", "Say \"check the starters\" to see the error."),
    ];

    /// <summary>A background job reporting in: worked, or failed with a reason.</summary>
    private void Beat(string job, bool ok, string detail = "")
    {
        var r = JobRecords.TryGetValue(job, out var existing) ? existing : JobRecords[job] = new JobRecord();
        var now = DateTimeOffset.Now;
        r.LastRun = now;
        r.LastFailed = !ok;
        r.Detail = Trim(detail, 200);
        if (ok) r.LastOk = now;
        if (!ok || now - _jobsSavedAt > TimeSpan.FromMinutes(5))
        {
            _jobsSavedAt = now;
            SaveJson(JobHealthPath, JobRecords);
        }
    }

    private void MaybeCheckJobs(DateTimeOffset now)
    {
        if (now - _jobsCheckedAt < TimeSpan.FromHours(1) || now - _jobsRunningSince < TimeSpan.FromMinutes(15)) return;
        _jobsCheckedAt = now;
        try
        {
            _jobProblems = JobHealth.Problems(JobExpectations, JobRecords, now, _jobsRunningSince, FeatureOn);
            var bad = _jobProblems.Select(p => p.Job.Job).ToHashSet();
            foreach (var job in JobExpectations.Where(j => !bad.Contains(j.Job))) ResolveAttentionByPrefix($"job-health:{job.Job}:");
            foreach (var p in _jobProblems)
                RaiseAttention($"job-health:{p.Job.Job}:{now.LocalDateTime:yyyyMMdd}", "Guardian", "Warning", $"{p.Job.Name} {p.Problem}", p.Job.Fix, "job_health");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Job health check skipped: {ex.Message}"); }
    }

    private async Task JobHealthReportAsync()
    {
        var now = DateTimeOffset.Now;
        _jobProblems = JobHealth.Problems(JobExpectations, JobRecords, now, _jobsRunningSince, FeatureOn);
        var bad = _jobProblems.ToDictionary(p => p.Job.Job, p => p.Problem);
        var sb = new StringBuilder($"GUARDIAN'S BACKGROUND JOBS · {now.LocalDateTime:HH:mm}\n");
        foreach (var job in JobExpectations)
        {
            var off = job.Feature.Length > 0 && !FeatureOn(job.Feature);
            JobRecords.TryGetValue(job.Job, out var r);
            var state = off ? "switched off" : bad.TryGetValue(job.Job, out var problem) ? $"✗ {problem}"
                : r?.LastOk is { } ok ? $"✓ last worked {ok.LocalDateTime:ddd HH:mm}{(r.Detail.Length > 0 && !r.LastFailed ? $" · {r.Detail}" : "")}" : "· not run yet";
            sb.AppendLine($"{job.Name}: {state}");
        }
        sb.AppendLine().Append("Connections themselves are tested by \"self check\".");
        ShowPaletteResponse(sb.ToString());
        await SpeakGuardianAsync(_jobProblems.Count == 0 ? "All my background jobs are working." :
            $"{_jobProblems.Count} of my jobs need a look. {_jobProblems[0].Job.Name} {_jobProblems[0].Problem}.", true);
    }

    /// <summary>For the web digest: what's not working.</summary>
    private List<string> JobHealthLines() => _jobProblems.Select(p => $"{p.Job.Name} {p.Problem}").ToList();
}
