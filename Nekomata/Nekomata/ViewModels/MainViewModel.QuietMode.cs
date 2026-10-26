using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nekomata.UI.ViewModels;

// Keeps Guardian from talking over the user. Unprompted speech (briefs, welcome back, debriefs, transition warnings
// and the rest) is held while:
//   * the user is on a call (see CallGuard);
//   * a calendar meeting with attendees is running and they aren't in a focus mission (if a mission is running they
//     evidently aren't in the meeting, and the overrun warning must still be heard);
//   * they have asked for quiet ("quiet for an hour", "do not disturb until 3").
// Speech they asked for - a voice conversation, or an answer to something they just typed - always plays. Held lines
// are kept (most recent two hours) and summarised in one line once the reason clears; the full text goes to the
// Guardian panel. The orb and heads-up overlay show that Guardian is quiet and why.
public partial class MainViewModel
{
    private static readonly TimeSpan SolicitedSpeechWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HeldSpeechRetention = TimeSpan.FromHours(2);
    private readonly List<(DateTimeOffset At, string Text)> _heldSpeech = [];
    private readonly DispatcherTimer _quietTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private DateTimeOffset? _quietUntil;
    private DateTimeOffset _solicitedSpeechUntil;
    private string _lastHoldReason = "";
    private bool _speakingHeldRecap;

    [ObservableProperty] private string hudQuietText = "";
    [ObservableProperty] private bool hasHudQuiet;

    private void InitialiseQuietMode()
    {
        _quietTimer.Tick += (_, _) => _ = EvaluateQuietStateAsync();
        _quietTimer.Start();
    }

    /// <summary>The user just asked for something (typed or palette); its spoken answer must not be held.</summary>
    private void MarkUserEngaged() => _solicitedSpeechUntil = DateTimeOffset.Now + SolicitedSpeechWindow;

    private bool IsSpeechSolicited() => _conversationActive || DateTimeOffset.Now < _solicitedSpeechUntil;

    /// <summary>On a call of any kind (see CallGuard).</summary>
    private bool IsOnPhoneCall => LiveCallReason() is not null;

    /// <summary>Why unprompted speech should be held right now, or null if Guardian may speak.</summary>
    private string? SpeechHoldReason()
    {
        var now = DateTimeOffset.Now;
        if (LiveCallReason() is { } live) return live;
        if (OffHoursHoldReason() is { } offDuty) return offDuty;
        // Do Not Disturb during a Guardian focus mission means "protect my focus", and Guardian is what protects it:
        // its overrun/transition warnings must still be heard. Calls and presenting silence it regardless.
        if (_teamsBusyReason is { } teams && !(MissionActive && teams == TeamsDoNotDisturb)) return teams;
        if (_quietUntil is { } until && now < until) return $"quiet until {until.LocalDateTime:HH:mm}";
        if (!MissionActive && CalendarEvents.Any(x => IsBlockingCalendarCommitment(x, now)))
            return "in a meeting";
        return null;
    }

    // The gate SpeakGuardianAsync applies. Returns true when the line was held instead of spoken.
    private bool HoldIfQuiet(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || _speakingHeldRecap) return false;
        // On a live call nothing is spoken, not even an answer he asked for: that one is already on screen, so it isn't
        // held for the recap either.
        if (LiveCallReason() is { } call && IsSpeechSolicited())
        {
            Think($"Not saying that aloud: you're {call}. It's on screen");
            return true;
        }
        if (IsSpeechSolicited()) return false;
        var reason = SpeechHoldReason();
        if (reason is null) return false;
        if (UrgentBreaksThrough(reason)) return false;
        _heldSpeech.Add((DateTimeOffset.Now, message.Trim()));
        _lastHoldReason = reason;
        Think($"Holding something to tell you: {reason}");
        RefreshQuietIndicator();
        return true;
    }

    private async Task EvaluateQuietStateAsync()
    {
        if (_quietUntil is { } until && DateTimeOffset.Now >= until) _quietUntil = null;
        RefreshQuietIndicator();
        if (SpeechHoldReason() is not null || _heldSpeech.Count == 0 || _conversationActive || _speakingHeldRecap) return;

        var cutoff = DateTimeOffset.Now - HeldSpeechRetention;
        var held = _heldSpeech.Where(x => x.At >= cutoff).ToList();
        var reason = _lastHoldReason;
        _heldSpeech.Clear();
        if (held.Count == 0 || !GuardianVoiceEnabled) return;

        var while_ = reason.StartsWith("quiet") ? "While you had me on quiet" : $"While you were {reason}";
        GuardianResponse = $"{while_}, I held {held.Count} update{(held.Count == 1 ? "" : "s")}:\n" +
                           string.Join("\n", held.Select(x => $"• {x.At.LocalDateTime:HH:mm} · {x.Text}"));
        GuardianPanelExpanded = true;
        var spoken = held.Count == 1
            ? $"{while_}: {held[0].Text}"
            : $"{while_}, I held {held.Count} updates. The latest: {FirstSentence(held[^1].Text)} The rest are in the Guardian panel.";
        _speakingHeldRecap = true;
        try { await SpeakGuardianAsync(spoken); }
        finally { _speakingHeldRecap = false; }
    }

    private void RefreshQuietIndicator()
    {
        var reason = SpeechHoldReason();
        HasHudQuiet = reason is not null;
        HudQuietText = reason is null ? "" : _heldSpeech.Count > 0 ? $"Quiet · {reason} · {_heldSpeech.Count} held" : $"Quiet · {reason}";
    }

    private void ApplyQuietModeCommand(string argument, int minutes)
    {
        var now = DateTimeOffset.Now;
        if (argument is "working" or "offduty" or "disable") { ApplyOffHoursCommand(argument); return; }
        if (argument == "off")
        {
            _quietUntil = null;
            ShowPaletteResponse("Voice is back on.");
            _ = EvaluateQuietStateAsync();
            return;
        }
        if (argument.StartsWith("until:", StringComparison.Ordinal) &&
            TimeSpan.TryParse(argument["until:".Length..], out var clock))
        {
            var target = new DateTimeOffset(DateTime.Today.Add(clock));
            if (target <= now) target = target.AddDays(1);
            _quietUntil = target;
        }
        else
            _quietUntil = now.AddMinutes(Math.Max(1, minutes));
        RefreshQuietIndicator();
        // Said in a conversation, this reply is solicited, so it is spoken; everything unprompted waits.
        ShowPaletteResponse($"Quiet until {_quietUntil.Value.LocalDateTime:HH:mm}. I'll hold anything I'd have said and catch you up after. Say \"you can talk again\" to end it early.");
    }
}
