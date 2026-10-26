using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Nekomata.Models.Planning;
using System.IO;
using System.Windows;

namespace Nekomata.UI.ViewModels;

// "Welcome back": when the user unlocks the PC after a real absence, Guardian greets them
// and says, in two or three lines, what changed while they were away - new attention items
// (most important first) and whether a meeting is running or about to start. The same
// text is left in the Guardian panel so it can be read if voice is off.
public partial class MainViewModel
{
    private static readonly TimeSpan WelcomeBackMinimumAbsence = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WelcomeBackMaximumAbsence = TimeSpan.FromHours(10);
    private static readonly TimeSpan WelcomeBackSettleDelay = TimeSpan.FromSeconds(3);

    private DateTimeOffset? _awaySince;
    private HashSet<string> _attentionKeysAtLock = new(StringComparer.OrdinalIgnoreCase);
    private bool _welcomingBack;

    private void InitialiseWelcomeBack()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        if (Application.Current is not null)
            Application.Current.Exit += (_, _) => SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    // SessionSwitch is raised on a SystemEvents thread, so hop to the UI thread before
    // touching AttentionItems or any bound state.
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                dispatcher.BeginInvoke(MarkAway);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                dispatcher.BeginInvoke(() => _ = WelcomeBackAsync());
                break;
        }
    }

    private void MarkAway()
    {
        if (_awaySince is not null) return;
        _awaySince = DateTimeOffset.Now;
        _attentionKeysAtLock = VisibleAttentionItems.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WelcomeBackAsync()
    {
        var awaySince = _awaySince;
        _awaySince = null;
        if (awaySince is null || _welcomingBack || !_initialLoadCompleted) return;

        var now = DateTimeOffset.Now;
        var away = now - awaySince.Value;
        if (away < WelcomeBackMinimumAbsence || away > WelcomeBackMaximumAbsence) return;

        // Before the working day starts, or before today's morning briefing has played,
        // stay quiet: the morning briefing is the first-thing greeting and covers the same
        // ground. There is deliberately no end-of-day cut-off - if the user is back at the
        // desk at 7pm, they are working and the catch-up is still useful.
        var hours = _services.GetService<WorkingDaySettings>() ?? new WorkingDaySettings();
        if (DateTime.Now < hours.GetStart(DateTime.Today))
        {
            System.Diagnostics.Debug.WriteLine("Welcome back skipped: before the working day.");
            return;
        }
        if (!File.Exists(MorningBriefingAnnouncedPath))
        {
            System.Diagnostics.Debug.WriteLine("Welcome back skipped: today's morning briefing has not played yet.");
            return;
        }

        _welcomingBack = true;
        try
        {
            // Give the timers that fire on resume a moment to raise anything new.
            await Task.Delay(WelcomeBackSettleDelay);
            var message = ComposeWelcomeBack(awaySince.Value, away, DateTimeOffset.Now);
            GuardianResponse = message;
            MarkCaughtUp();
            await SpeakGuardianAsync(message, interrupt: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Welcome-back briefing failed safely: {ex}");
        }
        finally
        {
            _welcomingBack = false;
        }
    }

    private string ComposeWelcomeBack(DateTimeOffset awaySince, TimeSpan away, DateTimeOffset now)
    {
        var greeting = UserFirstName.Length > 0 ? $"Welcome back, {UserFirstName}." : "Welcome back.";
        var parts = new List<string> { $"{greeting} You were away {FormatAbsence(away)}." };
        var arrived = VisibleAttentionItems
            .Where(x => !_attentionKeysAtLock.Contains(x.Key) && x.CreatedAt >= awaySince.AddMinutes(-1))
            .OrderByDescending(x => SeverityRank(x.Severity))
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        if (arrived.Count == 0)
            parts.Add("Nothing new needs you.");
        else
        {
            parts.Add(arrived.Count == 1
                ? $"One new thing needs you: {SpokenTitle(arrived[0].Title)}."
                : $"{arrived.Count} new things need you. Most important: {SpokenTitle(arrived[0].Title)}.");
            if (arrived.Count > 1)
                parts.Add($"Then {SpokenTitle(arrived[1].Title)}.");
        }

        var teams = TeamsSinceLine(awaySince);
        if (teams.Length > 0) parts.Add(teams);

        var active = _nowTimelineEvent;
        var next = _nextTimelineEvent;
        if (active is not null && !active.IsAllDay && active.Start <= now && active.End > now && !IsWorkingOnCalendarEvent(active))
            parts.Add($"{CleanCalendarObjectiveTitle(active.Subject)} started at {active.Start.ToLocalTime():HH:mm}.");
        else if (next is not null && !next.IsAllDay && next.Start > now && next.Start - now <= TimeSpan.FromMinutes(60))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((next.Start - now).TotalMinutes));
            parts.Add($"{CleanCalendarObjectiveTitle(next.Subject)} starts in {minutes} minute{(minutes == 1 ? "" : "s")}.");
        }

        return string.Join(" ", parts);
    }

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "warning" => 2,
        _ => 1
    };

    // Attention titles are written for the screen ("Reply needed · #4211 · VPN down");
    // turn the separators into pauses so they read naturally aloud.
    private static string SpokenTitle(string title) =>
        title.Replace(" · ", ", ").Trim().TrimEnd('.');

    private static string FormatAbsence(TimeSpan away)
    {
        var totalMinutes = (int)Math.Round(away.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes} minutes";
        var hourText = hours == 1 ? "an hour" : $"{hours} hours";
        return minutes < 5 ? hourText : $"{hourText} and {minutes} minutes";
    }
}
