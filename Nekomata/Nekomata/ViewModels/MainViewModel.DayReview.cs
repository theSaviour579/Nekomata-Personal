using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Planning;
using Nekomata.Models.Missions;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool dayReviewVisible;
    [ObservableProperty] private GuardianDayReview dayReview = new();
    [ObservableProperty] private string adHocObjectiveTitle = "";
    [ObservableProperty] private string adHocObjectiveOutcome = "";
    [ObservableProperty] private string adHocObjectiveStartedAtText = DateTime.Now.AddHours(-1).ToString("g");
    [ObservableProperty] private string adHocObjectiveFinishedAtText = DateTime.Now.ToString("g");
    [ObservableProperty] private string adHocObjectiveStatus = "";
    private bool _dayReviewLoading;
    [ObservableProperty] private string wrapUpNotes = "";
    [ObservableProperty] private string wrapUpStatus = "";
    private DateTime _wrapUpDate;
    private bool _wrapUpCompleted;
    public System.Collections.ObjectModel.ObservableCollection<WrapUpObjective> WrapUpObjectives { get; } = [];
    public string[] WrapUpDecisionOptions { get; } = ["No change", "Continue next workday", "Defer until", "Remove from next plan"];
    private sealed class WrapUpDraft
    {
        public string Notes { get; set; } = "";
        public bool Completed { get; set; }
        public List<WrapUpObjective> Objectives { get; set; } = [];
    }
    private static string WrapUpPath(DateTime day) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "daily-wrapups", $"{day:yyyy-MM-dd}.json");

    private void SaveWrapUp(bool completed)
    {
        var path = WrapUpPath(_wrapUpDate);
        if (WrapUpObjectives.Any(x => x.Decision == "Defer until" && (x.DeferUntil == null || x.DeferUntil.Value.Date <= _wrapUpDate)))
            throw new InvalidOperationException("Choose a future date for every deferred objective.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", System.Text.Json.JsonSerializer.Serialize(new WrapUpDraft { Notes = WrapUpNotes, Completed = completed || _wrapUpCompleted, Objectives = WrapUpObjectives.ToList() }));
        File.Move(path + ".tmp", path, true);
        _wrapUpCompleted |= completed;
    }

    private void EvaluateEndOfDayReview()
    {
        try
        {
            if (_dayReviewLoading || DayReviewVisible || IsInitialLoading || MissionActive ||
                DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                CalendarEvents.Any(x => x.Start <= DateTimeOffset.Now && x.End > DateTimeOffset.Now)) return;
            var profile = _personalProfile.Current;
            var settings = _services.GetRequiredService<WorkingDaySettings>();
            var wrapUpAt = profile.WorkScheduleConfigured
                ? DateTime.Today.Add(profile.WrapUpTime)
                : settings.GetEnd(DateTime.Today).AddMinutes(-15);
            if (DateTime.Now < wrapUpAt || File.Exists(DayReviewDismissedPath)) return;
            if (CanPresentRoutinePrompt(Nekomata.Core.Guardian.Anticipation.GuardianPromptKind.WrapUp)) _ = ShowDayReviewSafelyAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"End-of-day review trigger failed: {ex}");
        }
    }

    private async Task ShowDayReviewSafelyAsync()
    {
        try { await ShowDayReviewAsync(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"End-of-day review failed: {ex}");
            DayReviewVisible = false;
        }
    }

    [RelayCommand]
    private async Task ShowDayReviewAsync()
    {
        if (_dayReviewLoading) return;
        _dayReviewLoading = true;
        try
        {
            DayReview = await BuildPersonalDayReviewAsync();
            _wrapUpDate = DateTime.Today;
            var path = WrapUpPath(_wrapUpDate);
            var draft = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<WrapUpDraft>(File.ReadAllText(path)) : null;
            WrapUpNotes = draft?.Notes ?? "";
            _wrapUpCompleted = draft?.Completed == true;
            WrapUpObjectives.Clear();
            foreach (var objective in WrapUpPlanning.Build(Workspace.Tasks, _wrapUpDate))
            {
                var saved = draft?.Objectives.FirstOrDefault(x => x.Key == objective.Key);
                if (saved != null) { objective.Decision = saved.Decision; objective.DeferUntil = saved.DeferUntil; }
                WrapUpObjectives.Add(objective);
            }
            // Retain choices even if a source is temporarily absent from the refreshed snapshot.
            foreach (var saved in draft?.Objectives ?? [])
                if (!WrapUpObjectives.Any(x => x.Key == saved.Key) &&
                    !Workspace.Tasks.Any(x => x.Id == saved.TaskId && (x.IsCompleted || x.Completed)))
                    WrapUpObjectives.Add(saved);
            WrapUpStatus = draft?.Completed == true ? "Previously completed — you can update your handover." : "Draft — nothing is sent or changed in source systems.";
            DayReviewVisible = true;
            OnPropertyChanged(nameof(ReconciledWorkSummary));
        }
        finally { _dayReviewLoading = false; }
    }

    [RelayCommand]
    private async Task AddAdHocObjectiveAsync()
    {
        var title = AdHocObjectiveTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            AdHocObjectiveStatus = "Enter a short objective title.";
            return;
        }

        if (!RetrospectiveObjectiveInput.TryParseToday(
                AdHocObjectiveStartedAtText, AdHocObjectiveFinishedAtText,
                DateTime.Today, DateTime.Now, out var period, out var validationError))
        {
            AdHocObjectiveStatus = validationError;
            return;
        }

        var startedAt = period!.StartedAt;
        var finishedAt = period.FinishedAt;
        var elapsed = finishedAt - startedAt;
        var identitySource = $"{title.Trim().ToUpperInvariant()}|{startedAt:O}|{finishedAt:O}";
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identitySource)))[..24];

        var existing = await _missionSessionRepository.GetBetweenAsync(DateTime.Today, DateTime.Today.AddDays(1));
        var alreadySaved = existing.Any(item =>
            item.SourceType.Equals("AdHoc", StringComparison.OrdinalIgnoreCase) &&
            item.Title.Equals(title, StringComparison.OrdinalIgnoreCase) &&
            item.StartedAt == startedAt && item.FinishedAt == finishedAt);

        if(!alreadySaved && existing.Any(item => item.StartedAt < finishedAt && item.FinishedAt > startedAt))
        {
            if(System.Windows.MessageBox.Show("This period overlaps an existing work session. Save it anyway? Existing records will not be changed.",
                "Overlapping work",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Warning)!=System.Windows.MessageBoxResult.Yes)
            { AdHocObjectiveStatus="Not saved. Adjust the time or duration to avoid double-counting."; return; }
        }

        if (!alreadySaved)
        {
            await _missionSessionRepository.SaveAsync(new MissionSession
            {
                Title = title,
                SourceType = "AdHoc",
                EstimatedDurationMinutes = Math.Max(1, (int)Math.Ceiling(elapsed.TotalMinutes)),
                ActualDurationMinutes = Math.Max(1, (int)Math.Ceiling(elapsed.TotalMinutes)),
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                Completed = true,
                GuardianDecision = "Added retrospectively by the user.",
                RecommendationReason = string.IsNullOrWhiteSpace(AdHocObjectiveOutcome) ? "Unplanned work completed during the day." : AdHocObjectiveOutcome.Trim()
            });
        }

        await _services.GetRequiredService<WorkdayJournalService>().RecordIfAbsentAsync(new WorkdayEvent
        {
            EventType = "ObjectiveCompleted", Category = "Mission", Title = "Ad-hoc objective completed",
            Narrative = $"{title} added retrospectively ({(int)Math.Ceiling(elapsed.TotalMinutes)}m). {AdHocObjectiveOutcome.Trim()}",
            Outcome = string.IsNullOrWhiteSpace(AdHocObjectiveOutcome) ? "Completed" : AdHocObjectiveOutcome.Trim(), Significance = 75, ExternalId = $"adhoc:{identity}",
            OccurredAt = new DateTimeOffset(finishedAt)
        });

        var calendarNote = "";
        var calendarSaved = false;
        try
        {
            var marker = $"NEKOMATA:ADHOC:{identity}";
            await _services.GetRequiredService<ICalendarService>().CreateFocusEventAsync(
                $"Completed work · {title}", startedAt, finishedAt, marker);
            calendarSaved = true;
            calendarNote = " Calendar entry added.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ad-hoc objective calendar backfill failed: {ex}");
            calendarNote = " Saved to the digest; calendar backfill was unavailable.";
        }

        if(calendarSaved)
        {
            AdHocObjectiveTitle = "";
            AdHocObjectiveOutcome = "";
        }
        AdHocObjectiveStatus = alreadySaved
            ? $"That objective was already in today's digest.{calendarNote}"
            : $"Objective added.{calendarNote}";
        DayReview = await BuildPersonalDayReviewAsync();
        await RefreshAnalyticsAsync();
    }

    [RelayCommand]
    private void PlanTomorrowFromReview()
    {
        try { SaveWrapUp(false); File.WriteAllText(DayReviewDismissedPath, DateTimeOffset.Now.ToString("O")); }
        catch (Exception ex) { WrapUpStatus = "Could not save: " + ex.Message; return; }
        DayReviewVisible = false;
        WorkspaceMode = Models.Workspace.WorkspaceMode.Calendar;
        ChatInput = $"Plan tomorrow. Roll forward {DayReview.RollForwardCount} unfinished commitments and protect the highest-value item in the strongest evidence-backed morning focus window. Use predicted calendar requirement, not the raw entered estimate.";
        GuardianPanelExpanded = true;
    }

    [RelayCommand]
    private void ReviewDayOutcomes()
    {
        try { SaveWrapUp(false); File.WriteAllText(DayReviewDismissedPath, DateTimeOffset.Now.ToString("O")); }
        catch (Exception ex) { WrapUpStatus = "Could not save: " + ex.Message; return; }
        DayReviewVisible = false;
        WorkspaceMode = Models.Workspace.WorkspaceMode.Diagnostics;
    }

    [RelayCommand]
    private void DismissDayReview()
    {
        try { SaveWrapUp(false); }
        catch (Exception ex) { WrapUpStatus = "Could not save: " + ex.Message; return; }
        DayReviewVisible = false;
        Directory.CreateDirectory(Path.GetDirectoryName(DayReviewDismissedPath)!);
        File.WriteAllText(DayReviewDismissedPath, DateTimeOffset.Now.ToString("O"));
    }

    [RelayCommand]
    private async Task CompleteWrapUpAsync()
    {
        try
        {
            if (_wrapUpDate != DateTime.Today) { WrapUpStatus = "The date has changed. Save for later and reopen today's wrap-up."; return; }
            SaveWrapUp(false);
            await _services.GetRequiredService<WorkdayJournalService>().RecordOncePerDayAsync(new WorkdayEvent
            {
                EventType = "DayClosed", Category = "Review", Title = "Daily wrap-up completed", Significance = 90,
                Narrative = $"{DayReview.ObjectivesCompleted}/{DayReview.ObjectivesPlanned} planned objectives completed. Recorded work: {DayReview.CompletedLabel}. Handover: {WrapUpNotes}",
                BusinessValue = Analytics.BusinessValueDeliveredToday, Outcome = "Day captured"
            });
            SaveWrapUp(true);
            Directory.CreateDirectory(Path.GetDirectoryName(DayReviewDismissedPath)!);
            File.WriteAllText(DayReviewDismissedPath, DateTimeOffset.Now.ToString("O"));
            DayReviewVisible = false;
        }
        catch (Exception ex) { WrapUpStatus = "Wrap-up could not be completed: " + ex.Message; }
    }

    private static string DayReviewDismissedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal",
        $"day-review-{DateTime.Today:yyyy-MM-dd}.done");
}
