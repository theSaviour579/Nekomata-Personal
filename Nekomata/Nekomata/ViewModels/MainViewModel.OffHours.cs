using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Planning;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// Out-of-hours mode. Outside working hours, at weekends and on bank holidays Guardian goes off duty:
//   * unprompted speech is held (anything the user asks for is still answered);
//   * new Needs-you items wait for the briefing instead of chiming/flashing;
//   * the proactive jobs (approvals scan, project pulse, invites, knowledge drafts, prep packs, desk hygiene...) pause.
// Critical things still get through: anything Critical or marked urgent, and a manager (or urgent) Teams message.
// "I'm working" lifts it for four hours; "off duty" puts it back. On the first working morning after a weekend or
// bank holiday Guardian opens with a catch-up covering everything since the end of the last working day.
public partial class MainViewModel
{
    public sealed class OffHoursConfig
    {
        public bool Enabled { get; set; } = true;
        public DateTime? LastBreakCatchUp { get; set; }
    }

    private static string OffHoursPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "off-hours.json");
    private OffHoursConfig? _offHoursConfig;
    private OffHoursConfig OffHoursSettings => _offHoursConfig ??= LoadJson<OffHoursConfig>(OffHoursPath) ?? new();
    private DateTimeOffset? _workingAnywayUntil;
    private DateTimeOffset _criticalSpeechUntil;
    private DateTimeOffset? _homeReadyAt;

    private void SaveOffHoursSettings() => SaveJson(OffHoursPath, OffHoursSettings);

    /// <summary>True when Guardian is off duty: out of hours, the mode is on, and the user hasn't said he's working.</summary>
    private bool OffHoursActive
    {
        get
        {
            var now = DateTimeOffset.Now;
            if (!OffHoursSettings.Enabled || _workingAnywayUntil is { } until && now < until) return false;
            return OffHours.IsOutOfHours(now.LocalDateTime, _services.GetRequiredService<WorkingDaySettings>());
        }
    }

    private static string OffHoursReason(DateTime local) =>
        OffHours.IsBankHoliday(local.Date) ? "off duty · bank holiday"
        : local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "off duty · weekend"
        : "off duty · out of hours";

    /// <summary>What still breaks through when off duty.</summary>
    private static bool IsOffHoursCritical(string key, string severity, string title) =>
        severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("incident", StringComparison.OrdinalIgnoreCase) ||
        (key.StartsWith("teams:", StringComparison.OrdinalIgnoreCase) && severity.Equals("High", StringComparison.OrdinalIgnoreCase)) ||
        System.Text.RegularExpressions.Regex.IsMatch(title, @"\burgent\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Applied in RaiseAttention: off duty, a non-critical item waits for the briefing rather than chiming.</summary>
    private Nekomata.Models.Guardian.GuardianInterruptionDecision ApplyOffHours(
        Nekomata.Models.Guardian.GuardianInterruptionDecision routing, string key, string severity, string title)
    {
        if (!OffHoursActive) return routing;
        if (IsOffHoursCritical(key, severity, title))
        {
            _criticalSpeechUntil = DateTimeOffset.Now.AddMinutes(1);
            return routing;
        }
        if (routing.Route is Nekomata.Models.Guardian.GuardianInterruptionRoute.Immediate or Nekomata.Models.Guardian.GuardianInterruptionRoute.NaturalBreak)
            return routing with
            {
                Route = Nekomata.Models.Guardian.GuardianInterruptionRoute.Briefing,
                Explanation = $"Off duty, so this waits for your next briefing. {routing.Explanation}".Trim()
            };
        return routing;
    }

    /// <summary>The off-duty part of SpeechHoldReason.</summary>
    private string? OffHoursHoldReason() =>
        OffHoursActive && DateTimeOffset.Now >= _criticalSpeechUntil ? OffHoursReason(DateTime.Now) : null;

    private void ApplyOffHoursCommand(string argument)
    {
        var now = DateTimeOffset.Now;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        switch (argument)
        {
            case "working":
                _workingAnywayUntil = now.AddHours(4);
                RefreshQuietIndicator();
                ShowPaletteResponse(OffHours.IsOutOfHours(now.LocalDateTime, settings)
                    ? $"Understood, I'm on duty until {_workingAnywayUntil.Value.LocalDateTime:HH:mm}. Say \"off duty\" when you're done."
                    : "You're in working hours anyway, so I'm already on duty.");
                return;
            case "offduty":
                _workingAnywayUntil = null;
                if (!OffHoursSettings.Enabled) { OffHoursSettings.Enabled = true; SaveOffHoursSettings(); }
                RefreshQuietIndicator();
                ShowPaletteResponse(OffHours.IsOutOfHours(now.LocalDateTime, settings)
                    ? "Off duty. I'll only interrupt for something critical or your manager, and catch you up when you're back."
                    : $"Out-of-hours mode is on. It starts at {settings.EndTime:hh\\:mm}.");
                return;
            case "disable":
                OffHoursSettings.Enabled = false; SaveOffHoursSettings();
                RefreshQuietIndicator();
                ShowPaletteResponse("Out-of-hours mode is off. I'll behave the same at any hour. Say \"off duty\" to turn it back on.");
                return;
        }
    }

    /// <summary>First working morning after a weekend or bank holiday: one catch-up from the end of the last working day.</summary>
    private void MaybeCatchUpAfterBreak(DateTimeOffset now)
    {
        if (!_initialLoadCompleted || _catchingUp || OffHoursActive || !OffHoursSettings.Enabled) return;
        _homeReadyAt ??= now;
        if (now - _homeReadyAt < TimeSpan.FromMinutes(2) || _guardianSpeakingDepth > 0 || _conversationActive) return;
        var today = DateTime.Today;
        if (OffHoursSettings.LastBreakCatchUp?.Date == today) return;
        var breakName = OffHours.BreakName(today);
        if (breakName.Length == 0) return;
        OffHoursSettings.LastBreakCatchUp = today;
        SaveOffHoursSettings();
        var since = new DateTimeOffset(OffHours.PreviousWorkingDayEnd(now.LocalDateTime, _services.GetRequiredService<WorkingDaySettings>()));
        Think($"Catching you up on {breakName}");
        _ = WhatDidIMissAsync(since, onDemand: false, label: breakName);
    }
}
