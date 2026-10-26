using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.UI.Windows;

namespace Nekomata.UI.ViewModels;

// State behind the heads-up overlay (GuardianHudWindow). Text is recomputed on a short
// timer rather than wired into every source, because "in 12 min" style countdowns need a
// clock anyway. Visibility and position persist across restarts.
public partial class MainViewModel
{
    private sealed class HudSettings
    {
        public bool Visible { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
    }

    private static readonly TimeSpan HudDeadlineHorizon = TimeSpan.FromHours(24);
    private static readonly TimeSpan HudDeadlineUrgency = TimeSpan.FromMinutes(60);

    private readonly DispatcherTimer _hudTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private GuardianHudWindow? _hudWindow;
    private HudSettings _hudSettings = new();

    [ObservableProperty] private string hudNextText = "Calendar clear";
    [ObservableProperty] private string hudMissionText = "";
    [ObservableProperty] private bool hasHudMission;
    [ObservableProperty] private string hudDeadlineText = "";
    [ObservableProperty] private bool hasHudDeadline;
    [ObservableProperty] private bool hudDeadlineUrgent;
    [ObservableProperty] private string hudAttentionText = "";
    [ObservableProperty] private bool hasHudAttention;

    private static string HudSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "heads-up.json");

    private void InitialiseHud()
    {
        try
        {
            if (File.Exists(HudSettingsPath))
                _hudSettings = JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(HudSettingsPath)) ?? new();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Heads-up settings unreadable: {ex.Message}"); }

        _hudTimer.Tick += (_, _) => RefreshHud();
        if (_hudSettings.Visible)
            Application.Current?.Dispatcher.BeginInvoke(() => SetHudVisible(true), DispatcherPriority.ApplicationIdle);
    }

    [RelayCommand]
    private void ToggleHud() => SetHudVisible(_hudWindow is null);

    public void SetHudVisible(bool visible)
    {
        if (visible && _hudWindow is null)
        {
            RefreshHud();
            _hudWindow = new GuardianHudWindow(this);
            var area = SystemParameters.WorkArea;
            if (_hudSettings.Left is { } left && _hudSettings.Top is { } top &&
                left >= SystemParameters.VirtualScreenLeft && top >= SystemParameters.VirtualScreenTop &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 20)
            {
                _hudWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _hudWindow.Left = left;
                _hudWindow.Top = top;
            }
            else
            {
                // First run: centred along the top edge of the primary work area.
                _hudWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _hudWindow.Top = area.Top;
                _hudWindow.Loaded += (_, _) => _hudWindow!.Left = area.Left + (area.Width - _hudWindow.ActualWidth) / 2;
            }
            _hudWindow.Closed += (_, _) => { _hudWindow = null; _hudTimer.Stop(); };
            _hudWindow.Show();
            _hudTimer.Start();
        }
        else if (!visible && _hudWindow is not null)
        {
            var window = _hudWindow;
            _hudWindow = null;
            _hudTimer.Stop();
            window.Close();
        }

        if (_hudSettings.Visible != visible)
        {
            _hudSettings.Visible = visible;
            SaveHudSettings();
        }
    }

    public void RememberHudPosition(double left, double top)
    {
        _hudSettings.Left = left;
        _hudSettings.Top = top;
        SaveHudSettings();
    }

    private void SaveHudSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HudSettingsPath)!);
            File.WriteAllText(HudSettingsPath, JsonSerializer.Serialize(_hudSettings));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Heads-up settings not saved: {ex.Message}"); }
    }

    private void RefreshHud()
    {
        try
        {
            var now = DateTimeOffset.Now;

            var current = CalendarEvents
                .Where(x => !x.IsAllDay && x.Start <= now && x.End > now)
                .OrderBy(x => x.End)
                .FirstOrDefault();
            var next = CalendarEvents
                .Where(x => !x.IsAllDay && x.Start > now && x.Start.LocalDateTime.Date == DateTime.Today)
                .OrderBy(x => x.Start)
                .FirstOrDefault();
            if (current is not null && !IsWorkingOnCalendarEvent(current))
                HudNextText = $"In {CleanCalendarObjectiveTitle(current.Subject)} · until {current.End.LocalDateTime:HH:mm}";
            else if (next is not null)
            {
                var minutes = (int)Math.Ceiling((next.Start - now).TotalMinutes);
                HudNextText = minutes <= 90
                    ? $"{CleanCalendarObjectiveTitle(next.Subject)} in {minutes} min"
                    : $"{CleanCalendarObjectiveTitle(next.Subject)} at {next.Start.LocalDateTime:HH:mm}";
            }
            else
                HudNextText = "Calendar clear";

            HasHudMission = MissionActive && Workspace.CurrentMission is not null;
            HudMissionText = HasHudMission ? $"{Workspace.CurrentMission!.Title} · {MissionRemaining} left" : "";

            var localNow = DateTime.Now;
            var deadline = Workspace.Tasks
                .Where(x => !x.Completed && x.DueAt is not null && x.DueAt.Value - localNow <= HudDeadlineHorizon)
                .OrderBy(x => x.DueAt)
                .FirstOrDefault();
            HasHudDeadline = deadline is not null;
            if (deadline is not null)
            {
                var due = deadline.DueAt!.Value;
                var left = due - localNow;
                var id = Trim(deadline.Title, 40);
                HudDeadlineText = left < TimeSpan.Zero
                    ? $"{id} overdue {SpokenOverdue(-left)}"
                    : due.Date == localNow.Date ? $"{id} due {due:HH:mm}" : $"{id} due {due:ddd HH:mm}";
                HudDeadlineUrgent = left < HudDeadlineUrgency;
            }
            else
            {
                HudDeadlineText = "";
                HudDeadlineUrgent = false;
            }

            var count = ActiveAttentionCount;
            HasHudAttention = count > 0;
            HudAttentionText = count == 1 ? "1 needs you" : $"{count} need you";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Heads-up refresh failed safely: {ex.Message}");
        }
    }

    private static string SpokenOverdue(TimeSpan span) =>
        span.TotalMinutes < 90 ? $"{(int)Math.Ceiling(span.TotalMinutes)}m" :
        span.TotalHours < 48 ? $"{(int)Math.Round(span.TotalHours)}h" : $"{(int)Math.Round(span.TotalDays)}d";
}
