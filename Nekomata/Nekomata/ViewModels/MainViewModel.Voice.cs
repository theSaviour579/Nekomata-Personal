using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    private Task SpeakGuardianAsync(string? message, bool interrupt = false)
    {
        _guardianSpeech.Enabled = GuardianVoiceEnabled;
        return _guardianSpeech.SpeakAsync(message, interrupt);
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
}