using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Data.Repositories;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Missions;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private DailyActivityReconciliationResult activityReconciliation = new();
    [ObservableProperty] private string activityReconciliationStatus = "Evidence has not been checked yet.";
    private bool _reconcilingWork;
    private static string ReconciliationPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", $"reconciliation-{DateTime.Today:yyyy-MM-dd}.json");
    private Dictionary<string, string> ReadReconciliation() => File.Exists(ReconciliationPath)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ReconciliationPath)) ?? [] : [];
    public string ReconciledWorkSummary
    {
        get
        {
            try { var entries = ReadReconciliation(); return entries.Count == 0 ? "No automatic reconciliation recorded today." : string.Join("\n", entries.Values); }
            catch { return "Reconciliation history is currently unavailable; existing records have been preserved."; }
        }
    }

    private void RecordReconciliation(string key, string summary)
    {
        var entries = ReadReconciliation();
        if (!entries.TryAdd(key, summary)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ReconciliationPath)!);
        File.WriteAllText(ReconciliationPath + ".tmp", JsonSerializer.Serialize(entries));
        File.Move(ReconciliationPath + ".tmp", ReconciliationPath, true);
        OnPropertyChanged(nameof(ReconciledWorkSummary));
        _ = _services.GetRequiredService<WorkdayJournalService>().RecordIfAbsentAsync(new()
        {
            EventType = "WorkReconciled", Category = "Review", ExternalId = $"{DateTime.Today:yyyy-MM-dd}:{key}",
            Title = "Guardian reconciled work", Narrative = summary, Outcome = "Evidence observed", Significance = 45
        });
    }

    private async Task ReconcileWorkAsync()
    {
        if (_reconcilingWork || IsInitialLoading || _integrationRefreshBusy) return;
        _reconcilingWork = true;
        try
        {
            LoadPreflight();
            var changed = false;
            foreach (var step in _preflightDay!.Steps.Where(x => !x.Status.StartsWith("Done")))
            {
                if (!step.Id.StartsWith("carry:task:") || !long.TryParse(step.Id[11..], out var taskId)) continue;
                var task = Workspace.Tasks.FirstOrDefault(x => x.Id == taskId);
                if (task == null || !(task.Completed || WorkReconciliation.IsCompletedStatus(task.Status))) continue;
                RecordReconciliation(step.Id, $"Completed task confirmed: {task.Title}. Pre-flight updated; no time inferred.");
                step.Status = "Done automatically";
                changed = true;
            }
            if (changed) { SavePreflight(); _refreshPreflight?.Invoke(); }

        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Work reconciliation failed safely: " + ex.Message); }
        finally { _reconcilingWork = false; }
    }

    private async Task RefreshDailyActivityReconciliationAsync()
    {
        try
        {
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var localDay = DateTime.Today;
            var offset = TimeZoneInfo.Local.GetUtcOffset(localDay);
            var dayStart = new DateTimeOffset(settings.GetStart(localDay), offset);
            var dayEnd = new DateTimeOffset(settings.GetEnd(localDay), offset);
            var lunchStart = settings.IncludeLunchBreak
                ? new DateTimeOffset(settings.GetLunchStart(localDay), offset)
                : (DateTimeOffset?)null;
            var lunchEnd = settings.IncludeLunchBreak
                ? new DateTimeOffset(settings.GetLunchEnd(localDay), offset)
                : (DateTimeOffset?)null;

            var sessions = await _missionSessionRepository.GetBetweenAsync(localDay, localDay.AddDays(1));
            var journal = await _services.GetRequiredService<IWorkdayEventRepository>()
                .GetBetweenAsync(new DateTimeOffset(localDay, offset), new DateTimeOffset(localDay.AddDays(1), offset));
            var evidence = new List<ActivityEvidence>();

            foreach (var session in sessions.Where(x => x.FinishedAt > x.StartedAt && !x.Cancelled))
            {
                var segments = ReadMissionSegments(session);
                foreach (var segment in segments)
                    evidence.Add(new ActivityEvidence($"mission:{session.Id}:{segment.Start:O}", session.SourceType, session.Title,
                        LocalOffset(segment.Start), LocalOffset(segment.End), ActivityEvidenceKind.Work, 100));
            }

            foreach (var item in CalendarEvents.Where(x => !x.IsAllDay && x.End > x.Start))
                evidence.Add(new ActivityEvidence($"calendar:{item.Id}", "Calendar", item.Subject, item.Start, item.End,
                    ActivityEvidenceKind.Calendar, item.IsNekomataManaged ? 95 : 90, item.IsNekomataManaged));

            foreach (var item in journal)
                if (TryReadJournalInterval(item, out var interval)) evidence.Add(interval);

            ActivityReconciliation = _services.GetRequiredService<DailyActivityReconciliationEngine>()
                .Reconcile(dayStart, dayEnd, DateTimeOffset.Now, lunchStart, lunchEnd, evidence);
            ActivityReconciliationStatus = ActivityReconciliation.HasGaps
                ? "Review only the gaps you recognise. Nekomata will not infer work without your confirmation."
                : "The elapsed working day is fully explained by observed or confirmed evidence.";
        }
        catch (Exception ex)
        {
            ActivityReconciliationStatus = "Activity evidence could not be reconciled; existing records were not changed.";
            System.Diagnostics.Debug.WriteLine($"Daily activity reconciliation failed safely: {ex}");
        }
    }

    [RelayCommand]
    private async Task ConfirmActivityGapAsync(ActivityReconciliationGap? gap)
    {
        if (gap is null || string.IsNullOrWhiteSpace(gap.ReviewTitle))
        {
            ActivityReconciliationStatus = "Describe the work before recording this gap.";
            return;
        }

        var title = gap.ReviewTitle.Trim();
        var minutes = Math.Max(1, gap.Minutes);
        await _missionSessionRepository.SaveAsync(new MissionSession
        {
            Title = title,
            SourceType = "Reconciled",
            EstimatedDurationMinutes = minutes,
            ActualDurationMinutes = minutes,
            StartedAt = gap.Start.LocalDateTime,
            FinishedAt = gap.End.LocalDateTime,
            Completed = true,
            GuardianDecision = "Confirmed by the user during daily reconciliation.",
            RecommendationReason = gap.ReviewCategory,
            ActiveIntervalsJson = JsonSerializer.Serialize(new[] { new MissionWorkSegment(gap.Start.LocalDateTime, gap.End.LocalDateTime) }),
            SourceEventKey = $"reconciled:{gap.Start:O}:{gap.End:O}"
        });
        await _services.GetRequiredService<WorkdayJournalService>().RecordIfAbsentAsync(new WorkdayEvent
        {
            EventType = "ActivityGapConfirmed", Category = gap.ReviewCategory, Title = title,
            Narrative = $"User confirmed {minutes} minutes of previously unexplained work.", Outcome = "Recorded",
            ExternalId = $"activity-gap:{gap.Start:O}:{gap.End:O}", OccurredAt = gap.End,
            Metadata = JsonSerializer.Serialize(new { startedAt = gap.Start, endedAt = gap.End, kind = "Work" })
        });
        ActivityReconciliationStatus = $"Recorded {gap.TimeLabel} as {title}.";
        await RefreshDailyActivityReconciliationAsync();
        DayReview = await _services.GetRequiredService<GuardianDayReviewService>().BuildAsync(Workspace);
        await RefreshAnalyticsAsync();
    }

    [RelayCommand]
    private async Task MarkActivityGapNonWorkAsync(ActivityReconciliationGap? gap)
    {
        if (gap is null) return;
        await _services.GetRequiredService<WorkdayJournalService>().RecordIfAbsentAsync(new WorkdayEvent
        {
            EventType = "ActivityGapDismissed", Category = "Non-work", Title = "Confirmed non-work period",
            Narrative = $"User marked {gap.TimeLabel} as non-work; it will not be counted as delivered work.", Outcome = "Excluded",
            ExternalId = $"activity-gap-nonwork:{gap.Start:O}:{gap.End:O}", OccurredAt = gap.End,
            Metadata = JsonSerializer.Serialize(new { startedAt = gap.Start, endedAt = gap.End, kind = "NonWork" })
        });
        ActivityReconciliationStatus = $"Excluded {gap.TimeLabel} from recorded work.";
        await RefreshDailyActivityReconciliationAsync();
    }

    private static IReadOnlyList<MissionWorkSegment> ReadMissionSegments(MissionSession session)
    {
        try
        {
            var segments = JsonSerializer.Deserialize<List<MissionWorkSegment>>(session.ActiveIntervalsJson ?? "[]") ?? [];
            if (segments.Count > 0) return segments.Where(x => x.End > x.Start).ToList();
        }
        catch { }
        return [new MissionWorkSegment(session.StartedAt, session.FinishedAt)];
    }

    private static DateTimeOffset LocalOffset(DateTime value) =>
        new(value, TimeZoneInfo.Local.GetUtcOffset(value));

    private static bool TryReadJournalInterval(WorkdayEvent item, out ActivityEvidence evidence)
    {
        evidence = default!;
        if (item.EventType is not ("PhoneCallCompleted" or "ActivityGapDismissed")) return false;
        try
        {
            using var document = JsonDocument.Parse(item.Metadata);
            var root = document.RootElement;
            var startName = item.EventType == "PhoneCallCompleted" ? "StartedAt" : "startedAt";
            var endName = item.EventType == "PhoneCallCompleted" ? "EndedAt" : "endedAt";
            if (!root.TryGetProperty(startName, out var startNode) || !root.TryGetProperty(endName, out var endNode) ||
                !startNode.TryGetDateTimeOffset(out var start) || !endNode.TryGetDateTimeOffset(out var end) || end <= start) return false;
            var kind = item.EventType == "PhoneCallCompleted" ? ActivityEvidenceKind.Interruption : ActivityEvidenceKind.NonWork;
            evidence = new ActivityEvidence(item.ExternalId, item.EventType == "PhoneCallCompleted" ? "Call" : "User review",
                item.Title, start, end, kind, 100);
            return true;
        }
        catch { return false; }
    }
}
