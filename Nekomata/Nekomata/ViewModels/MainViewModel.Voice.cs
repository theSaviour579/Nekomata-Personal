using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Models.Planning;
using Nekomata.UI.Controls.Common;
using System.IO;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GuardianVoiceButtonLabel))]
    private bool guardianVoiceEnabled = true;

    public string GuardianVoiceButtonLabel =>
        GuardianVoiceEnabled ? "VOICE ON" : "VOICE OFF";

    [RelayCommand]
    private void ToggleGuardianVoice()
    {
        GuardianVoiceEnabled = !GuardianVoiceEnabled;
        _guardianSpeech.Enabled = GuardianVoiceEnabled;
        if (GuardianVoiceEnabled)
            _ = _guardianSpeech.SpeakAsync("Guardian voice enabled.");
    }

    private async Task SpeakGuardianAsync(string? message, bool interrupt = false)
    {
        if (GuardianVoiceEnabled && HoldIfQuiet(message)) return;
        message = Nekomata.Core.Guardian.GeneratedTextDeduplicator.RemoveRepeatedBlocks(message);
        message = BreakThroughPreface(message ?? "");
        Nekomata.Core.Guardian.FormalManner.Enabled = FeatureOn("formal-voice");
        Nekomata.Core.Guardian.FormalManner.UserFirstName = UserFirstName;
        message = Nekomata.Core.Guardian.FormalManner.Apply(message, unprompted: !IsSpeechSolicited());
        _guardianSpeech.Enabled = GuardianVoiceEnabled;
        _guardianSpeakingDepth++;
        _guardianUtteranceCount++;
        GuardianPresenceState = GuardianPresenceState.Speaking;
        if (_guardianSpeakingDepth == 1) { StartBargeInMonitor(); DuckMusic(true); }
        try
        {
            await _guardianSpeech.SpeakAsync(message, interrupt);
        }
        finally
        {
            _guardianSpeakingDepth = Math.Max(0, _guardianSpeakingDepth - 1);
            if (_guardianSpeakingDepth == 0) { StopBargeInMonitor(); DuckMusic(false); }
            if (_guardianSpeakingDepth == 0 && GuardianPresenceState == GuardianPresenceState.Speaking)
                GuardianPresenceState = GuardianPresenceState.Idle;
        }
    }

    private bool _announcingNewDay;

    private static string MorningBriefingAnnouncedPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal",
        $"morning-briefing-{DateTime.Today:yyyy-MM-dd}.done");

    // Keeps the "good morning" moment from being lost if the app is left running across
    // midnight: the splash-only path in LoadAsync only ever fires once, on a cold start.
    private void EvaluateMorningBriefingBoundary()
    {
        try
        {
            if (_announcingNewDay || !_initialLoadCompleted || IsInitialLoading) return;
            if (DateTime.Now < _services.GetRequiredService<WorkingDaySettings>().GetStart(DateTime.Today)) return;
            if (File.Exists(MorningBriefingAnnouncedPath)) return;
            _ = AnnounceNewCalendarDayAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Morning briefing day-boundary check failed: {ex}");
        }
    }

    private async Task AnnounceNewCalendarDayAsync()
    {
        _announcingNewDay = true;
        try
        {
            // Claim the briefing before doing any asynchronous work. Startup and the
            // midnight timer can overlap, and only one of them should deliver it.
            if (!TryMarkMorningBriefingAnnounced()) return;
            Workspace = await _workspaceCoordinator.RefreshAsync();
            OnPropertyChanged(nameof(GuardianSuggestedTasks));
            UpdateGreeting();
            await RefreshDailyBriefingContextAsync();
            await RefreshCalendarAwareObjectiveAsync();
            // The same briefing as a fresh start-up (fresh calendar, one definition of "meetings"). The older
            // workspace summary is only its fallback; using it here contradicted the real brief.
            await SpeakMorningBriefingWithWeeklyAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"New-day announcement failed: {ex}");
        }
        finally
        {
            _announcingNewDay = false;
        }
    }

    private static bool TryMarkMorningBriefingAnnounced()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MorningBriefingAnnouncedPath)!);
            using var marker = new FileStream(
                MorningBriefingAnnouncedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);
            using var writer = new StreamWriter(marker);
            writer.Write(DateTimeOffset.Now.ToString("O"));
            return true;
        }
        catch (IOException) when (File.Exists(MorningBriefingAnnouncedPath))
        {
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not record today's morning briefing: {ex.Message}");
            // A marker failure must not silence the briefing completely. The in-process
            // announcement guard still prevents the timer from starting it concurrently.
            return true;
        }
    }

    private Task SpeakMorningBriefingAsync()
    {
        var briefing = Workspace.Briefing;
        var yesterday = briefing.MissionsCompletedYesterday == 0
            ? "Yesterday was quiet, with no missions completed."
            : briefing.MissionsCompletedYesterday == 1
                ? $"You completed one mission yesterday, with {briefing.FocusTimeYesterdayFormatted} of focused work."
                : $"You completed {briefing.MissionsCompletedYesterday} missions yesterday, with {briefing.FocusTimeYesterdayFormatted} of focused work.";

        var today = ComposeSpokenTodaySummary(briefing);
        var meetings = ComposeSpokenMeetingSummary(briefing.MeetingSummary);

        return SpeakGuardianAsync(
            $"{briefing.Greeting} {yesterday} {today} {meetings}");
    }

    private static string ComposeSpokenTodaySummary(Nekomata.Models.Briefing.MorningBriefing briefing)
    {
        if (briefing.TasksDueToday == 0 && briefing.OverdueTasks == 0 && briefing.CriticalTasks == 0)
        {
            return string.IsNullOrWhiteSpace(briefing.ObjectiveTitle)
                ? "It looks like you have nothing pressing today. We have some room to choose where to make progress."
                : $"It looks like you have nothing pressing today. We should use the space to make progress on {briefing.ObjectiveTitle}.";
        }

        var parts = new List<string>();
        if (briefing.TasksDueToday > 0)
            parts.Add(briefing.TasksDueToday == 1 ? "one task due today" : $"{briefing.TasksDueToday} tasks due today");
        if (briefing.OverdueTasks > 0)
            parts.Add(briefing.OverdueTasks == 1 ? "one overdue item" : $"{briefing.OverdueTasks} overdue items");
        if (briefing.CriticalTasks > 0)
            parts.Add(briefing.CriticalTasks == 1 ? "one critical item" : $"{briefing.CriticalTasks} critical items");

        var workload = string.Join(", ", parts);
        var recommendation = string.IsNullOrWhiteSpace(briefing.ObjectiveTitle)
            ? string.Empty
            : $" I suggest we begin with {briefing.ObjectiveTitle}.";
        return $"There are {workload} on the board.{recommendation}";
    }

    private static string ComposeSpokenMeetingSummary(string? meetingSummary)
    {
        if (string.IsNullOrWhiteSpace(meetingSummary) ||
            meetingSummary.Contains("none with other attendees", StringComparison.OrdinalIgnoreCase))
        {
            return "You have no meetings planned in just yet.";
        }

        var details = meetingSummary.StartsWith("Meetings:", StringComparison.OrdinalIgnoreCase)
            ? meetingSummary["Meetings:".Length..].Trim()
            : meetingSummary.Trim();
        return $"As for meetings, you have {details}";
    }

    private Task SpeakEndOfDayReviewAsync()
    {
        var review = DayReview;
        var objectives = review.ObjectivesPlanned == 0
            ? "You didn't have planned objectives tracked today."
            : review.ObjectivesCompleted == review.ObjectivesPlanned
                ? $"You completed all {review.ObjectivesPlanned} planned objective{(review.ObjectivesPlanned == 1 ? "" : "s")} today."
                : $"You completed {review.ObjectivesCompleted} of {review.ObjectivesPlanned} planned objectives today.";
        var focusedTime = review.CompletedMinutes > 0 ? $" {review.CompletedLabel} of that was focused work." : string.Empty;
        var interruptions = review.InterruptionCount switch
        {
            0 => string.Empty,
            1 => " There was one interruption along the way.",
            _ => $" There were {review.InterruptionCount} interruptions along the way."
        };
        var rollForward = review.RollForwardCount switch
        {
            0 => " Nothing needs to roll forward.",
            1 => " One item will roll forward to tomorrow.",
            _ => $" {review.RollForwardCount} items will roll forward to tomorrow."
        };
        return SpeakGuardianAsync(
            $"That's the workday closed. {objectives}{focusedTime}{interruptions}{rollForward} {review.TomorrowSummary}");
    }
}
