using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Models.Planning;
using Nekomata.UI.Windows;

namespace Nekomata.UI.ViewModels;

/// <summary>The user's Guardian preferences: feature switches, thresholds, hours and weather location.</summary>
public sealed class GuardianPreferences
{
    public Dictionary<string, bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TimeSpan? WorkStart { get; set; }
    public TimeSpan? WorkEnd { get; set; }
    public int ProjectQuietDays { get; set; } = 9;
    public string WeatherLocation { get; set; } = "";
    /// <summary>Hours of meetings a week before Guardian suggests trimming (0 = off).</summary>
    public int MeetingBudgetHours { get; set; } = 12;
}

// The Guardian settings page ("settings"): one place for hours, background features, Teams managers and muted chats,
// thresholds, weather location, and which Microsoft account holds which permission (with sign-in, grant and sign-out).
public partial class MainViewModel
{
    private static string GuardianPreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "guardian-settings.json");
    private GuardianPreferences? _guardianPreferences;
    private GuardianPreferences Prefs => _guardianPreferences ??= LoadJson<GuardianPreferences>(GuardianPreferencesPath) ?? new();

    /// <summary>Background features that can be switched off. Key, name, what it does.</summary>
    private static readonly (string Key, string Name, string Description)[] GuardianFeatures =
    [
        ("music", "Music assist", "Pauses Spotify during calls and turns it down while Guardian speaks"),
        ("meeting-notes", "Notes after meetings", "A typed prompt a few minutes after each meeting with people: anything to note from it?"),
        ("formal-voice", "Formal manner", "Speaks like a formal British assistant and calls you Sir (\"stop calling me Sir\" turns it off)"),
        ("teams", "Teams watch", "Reads your chats every 90 seconds, flags managers, @mentions and urgent messages"),
        ("meeting-catchup", "Catch-up after meetings", "What came in while you were in a meeting of 25 minutes or more"),
        ("break-catchup", "Catch-up after weekends", "The first working morning after a weekend or bank holiday"),
        ("invites", "Calendar defence", "Assesses new invites and drafts declines or tentatives for you to confirm"),
        ("prep", "Meeting prep packs", "Builds a prep pack before meetings with people"),
        ("approvals", "Approvals inbox", "Spots approval requests in your mail"),
        ("projects", "Project autopilot", "Daily pulse on your projects; flags ones that have gone quiet"),
        ("duties", "Recurring duties", "Reminds you before each duty is due"),
        ("objectives", "Objectives", "Links finished work to your objectives each afternoon; flags ones that stall"),
        ("time", "Where my time goes", "Notes the kind of work every 15 seconds (never titles or content) for the time breakdown"),
        ("wellbeing", "Working-pattern nudges", "Late finishes, no lunch, long days, no focus time; at most once a week each"),
        ("meeting-load", "Meeting load", "Friday check of next week against your budget; monthly recurring-meeting review"),
        ("capture", "Capture from Teams and email", "Messages to yourself starting G: become tasks, reminders or notes"),
        ("place", "Office or home", "Recognises the office network and reminds you of office things when you arrive"),
        ("weather", "Weather on Home", "Live weather with animated conditions"),
        ("week-review", "Friday week in review", "Friday afternoon: a short review of the week drafted in Outlook (never sent)"),
    ];

    private bool FeatureOn(string key) => !Prefs.Features.TryGetValue(key, out var on) || on;

    /// <summary>At start-up: working hours from the settings page override the defaults.</summary>
    private void ApplyGuardianPreferences()
    {
        Nekomata.Core.Guardian.FormalManner.Enabled = FeatureOn("formal-voice");
        Nekomata.Core.Guardian.FormalManner.UserFirstName = UserFirstName;
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        if (Prefs.WorkStart is { } start) hours.StartTime = start;
        if (Prefs.WorkEnd is { } end && end > hours.StartTime) hours.EndTime = end;
    }

    private static readonly Dictionary<string, string[]> PermissionSetsExtra = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Transcripts"] = Nekomata.Integrations.MicrosoftGraph.Meetings.TeamsTranscriptService.Scopes,
    };

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenGuardianSettings()
    {
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        var vm = new GuardianSettingsViewModel(SaveGuardianSettingsAsync, LoadAccountRowsAsync, GuardianAccountActionAsync)
        {
            WorkStart = hours.StartTime.ToString(@"hh\:mm"),
            WorkEnd = hours.EndTime.ToString(@"hh\:mm"),
            OffHoursEnabled = OffHoursSettings.Enabled,
            Managers = string.Join(", ", TeamsConfig.Managers),
            NotManagers = string.Join(", ", TeamsConfig.NotManagers),
            LineManager = TeamsConfig.LineManager.Length > 0 ? TeamsConfig.LineManager : "(not found yet)",
            MutedChatCount = TeamsConfig.MutedChats.Count,
            ProjectQuietDays = Prefs.ProjectQuietDays,
            MeetingBudgetHours = Prefs.MeetingBudgetHours,
            WeatherLocation = Prefs.WeatherLocation,
        };
        foreach (var (key, name, description) in GuardianFeatures)
            vm.Features.Add(new GuardianFeatureToggle { Key = key, Name = name, Description = description, Enabled = FeatureOn(key) });
        var window = new GuardianSettingsWindow(vm) { Owner = Application.Current?.MainWindow };
        window.Show();
    }

    private Task SaveGuardianSettingsAsync(GuardianSettingsViewModel vm)
    {
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        var start = TimeSpan.Parse(vm.WorkStart);
        var end = TimeSpan.Parse(vm.WorkEnd);
        hours.StartTime = start;
        hours.EndTime = end;
        Prefs.WorkStart = start;
        Prefs.WorkEnd = end;
        Prefs.ProjectQuietDays = Math.Clamp(vm.ProjectQuietDays, 1, 90);
        Prefs.MeetingBudgetHours = Math.Clamp(vm.MeetingBudgetHours, 0, 50);
        var weatherChanged = !string.Equals(Prefs.WeatherLocation, vm.WeatherLocation.Trim(), StringComparison.OrdinalIgnoreCase);
        Prefs.WeatherLocation = vm.WeatherLocation.Trim();
        foreach (var toggle in vm.Features) Prefs.Features[toggle.Key] = toggle.Enabled;
        SaveJson(GuardianPreferencesPath, Prefs);

        OffHoursSettings.Enabled = vm.OffHoursEnabled;
        SaveOffHoursSettings();

        static List<string> Names(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TeamsConfig.Managers = Names(vm.Managers);
        TeamsConfig.NotManagers = Names(vm.NotManagers);
        if (vm.ClearMutedChats) TeamsConfig.MutedChats.Clear();
        SaveJson(TeamsSettingsPath, TeamsConfig);

        if (FeatureOn("teams")) { if (!_teamsTimer.IsEnabled) _teamsTimer.Start(); }
        else _teamsTimer.Stop();
        if (weatherChanged) _weatherLocationResolved = null;
        if (FeatureOn("weather")) _ = RefreshWeatherAsync(force: true);
        else HasWeather = false;
        RefreshQuietIndicator();
        Think("Settings saved");
        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<GuardianAccountRow>> LoadAccountRowsAsync()
    {
        var auth = _services.GetRequiredService<IMicrosoftAuthenticationService>();
        var sets = new Dictionary<string, string[]>(PermissionSetsExtra, StringComparer.OrdinalIgnoreCase);
        if (_services.GetService<MicrosoftGraphOptions>() is { Scopes.Length: > 0 } options) sets["Everyday"] = options.Scopes;
        var accounts = await auth.GetSignedInAccountsAsync(sets);
        return accounts.Select(a => new GuardianAccountRow
        {
            Username = a.Username,
            IsPrimary = a.IsPrimary,
            Permissions = string.Join("   ", a.Permissions.OrderBy(p => p.Key == "Everyday" ? 0 : 1).ThenBy(p => p.Key)
                .Select(p => $"{(p.Value ? "✓" : "✗")} {p.Key}"))
        }).ToList();
    }

    private async Task<string> GuardianAccountActionAsync(string action, string username)
    {
        var auth = _services.GetRequiredService<IMicrosoftAuthenticationService>();
        switch (action)
        {
            case "primary":
                await auth.SetPrimaryAccountAsync(username);
                return $"{username} is now the everyday account. Mail, calendar and Teams will use it from the next refresh.";
            case "signout":
                await auth.SignOutAsync(username);
                return $"Signed {username} out of Guardian.";
            case "everyday":
            case "add":
                var everyday = _services.GetService<MicrosoftGraphOptions>()?.Scopes ?? [];
                var who = await auth.SignInAsync(everyday);
                return action == "add" ? $"Added {who}." : $"Signed in again as {who}.";
            default:
                var name = PermissionSetsExtra.Keys.FirstOrDefault(k => k.Equals(action, StringComparison.OrdinalIgnoreCase));
                if (name is null) return "";
                var account = await auth.SignInAsync(PermissionSetsExtra[name]);
                return $"{name} permission granted with {account}. Pick an admin account in the sign-in window if your own can't consent.";
        }
    }
}
