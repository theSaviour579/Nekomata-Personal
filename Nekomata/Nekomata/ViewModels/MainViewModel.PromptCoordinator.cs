using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;
public partial class MainViewModel
{
    private DateTimeOffset _routinePromptsPausedUntil;
    private DateTimeOffset _routinePromptQuietUntil;
    private bool _promptPauseLoaded;
    public string RoutinePromptStatus => _routinePromptsPausedUntil > DateTimeOffset.Now
        ? $"Routine prompts paused until {_routinePromptsPausedUntil.LocalDateTime:HH:mm}; urgent alerts remain enabled."
        : "Routine prompts coordinated · urgent alerts take precedence.";
    private static bool IsUrgentAttention(GuardianAttentionItem item) => item.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
        item.Severity.Equals("High", StringComparison.OrdinalIgnoreCase) || item.Severity.Equals("Urgent", StringComparison.OrdinalIgnoreCase) || item.Key.StartsWith("halo:p1:", StringComparison.OrdinalIgnoreCase);
    partial void OnDayReviewVisibleChanged(bool value) { if (!value) RoutinePromptClosed(); }
    private static string PromptPausePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "routine-prompt-pause.txt");
    private void RoutinePromptClosed() => _routinePromptQuietUntil = DateTimeOffset.Now.AddMinutes(2);
    // A real commitment with other people that should hold back routine prompts. All-day entries ("Working from
    // home", leave cover), Guardian's own focus blocks and the user's personal blocks with no attendees (a "Daily wrap
    // up" slot, lunch, focus time) are not meetings; counting them held the end-of-day review back indefinitely.
    private static bool IsBlockingCalendarCommitment(Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent x, DateTimeOffset now) =>
        !x.IsAllDay && !x.IsNekomataManaged && x.Attendees.Count > 0 && !IsWrapUpSlot(x) && x.Start <= now && x.End > now;

    private static readonly System.Text.RegularExpressions.Regex WrapUpSlotTitle = new(
        @"\b(wrap[\s-]?up|end\s+of\s+(the\s+)?day|eod|close[\s-]?(down|out)|shut[\s-]?down|day\s+review|daily\s+review)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>A calendar slot the user keeps for closing the day ("Daily wrap up", "EOD review").</summary>
    private static bool IsWrapUpSlot(Nekomata.Integrations.MicrosoftGraph.Models.CalendarEvent x) =>
        !x.IsAllDay && WrapUpSlotTitle.IsMatch(x.Subject ?? "");

    private bool CanPresentRoutinePrompt(GuardianPromptKind kind)
    {
        try
        {
            if (!_promptPauseLoaded)
            {
                if (File.Exists(PromptPausePath) && DateTimeOffset.TryParse(File.ReadAllText(PromptPausePath), out var until)) _routinePromptsPausedUntil = until;
                _promptPauseLoaded = true;
            }
            var now = DateTimeOffset.Now;
            var occupied = IsInitialLoading || MissionActive || DayReviewVisible || _dayReviewLoading ||
                _preflightWindow != null || _meetingPreparationWindow != null || _nextActionWindow != null || _meetingCaptureWindow != null ||
                !CalendarLoaded || CalendarBusy || CalendarEvents.Any(x => IsBlockingCalendarCommitment(x, now)) ||
                // No popups while on a call, presenting (they would appear on the shared screen) or on Do Not Disturb.
                IsOnPhoneCall || _teamsBusyReason is not null;
            var urgent = VisibleAttentionItems.Any(IsUrgentAttention);
            var waiting = new List<GuardianPromptKind>();
            if (_meetingPreparationBusy || AttentionItems.Any(x => x.ActionKind == "meeting_preparation" && x.IsActive && !x.MeetingPromptShown && x.ExpiresAt > now)) waiting.Add(GuardianPromptKind.MeetingBrief);
            var end = _services.GetRequiredService<WorkingDaySettings>().GetEnd(now.LocalDateTime);
            var wrapUpSlotStarted = CalendarEvents.Any(x => IsWrapUpSlot(x) && x.Start <= now && x.End > now);
            if ((now.LocalDateTime >= end.AddMinutes(-15) || wrapUpSlotStarted) && !File.Exists(DayReviewDismissedPath)) waiting.Add(GuardianPromptKind.WrapUp);
            if (_preflightDay is { Presented: false } && PreflightDataReady && now.LocalDateTime.Hour < 12) waiting.Add(GuardianPromptKind.Preflight);
            return GuardianPromptPolicy.CanPresent(kind, waiting, occupied, urgent, now, _routinePromptsPausedUntil, _routinePromptQuietUntil);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Prompt coordination unavailable: " + ex.Message); return false; }
    }
    [RelayCommand]
    private void PauseRoutinePrompts()
    {
        try
        {
            var until = DateTimeOffset.Now.AddMinutes(15);
            Directory.CreateDirectory(Path.GetDirectoryName(PromptPausePath)!);
            File.WriteAllText(PromptPausePath + ".tmp", until.ToString("O"));
            File.Move(PromptPausePath + ".tmp", PromptPausePath, true);
            _routinePromptsPausedUntil = until; _promptPauseLoaded = true;
            OnPropertyChanged(nameof(RoutinePromptStatus));
            _nextActionWindow?.Close();
            MessageBox.Show($"Routine automatic prompts paused until {until.LocalDateTime:HH:mm}. Urgent alerts and manual navigation remain available. Background checks continue.", "Guardian prompts");
        }
        catch (Exception ex) { MessageBox.Show("Could not save prompt pause: " + ex.Message); }
    }
    [RelayCommand]
    private void ResumeRoutinePrompts()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PromptPausePath)!);
            File.WriteAllText(PromptPausePath, DateTimeOffset.MinValue.ToString("O"));
            _routinePromptsPausedUntil = DateTimeOffset.MinValue; _promptPauseLoaded = true;
            OnPropertyChanged(nameof(RoutinePromptStatus));
        }
        catch (Exception ex) { MessageBox.Show("Could not resume prompts: " + ex.Message); }
    }
}
