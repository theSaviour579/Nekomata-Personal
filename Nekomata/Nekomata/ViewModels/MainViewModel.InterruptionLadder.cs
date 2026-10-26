using System.IO;

namespace Nekomata.UI.ViewModels;

// The interruption ladder for what Guardian says aloud. Routine lines wait while the user is in a meeting, on quiet,
// on Do Not Disturb or off duty, and are recapped at the next natural gap (QuietMode). Urgent ones - something
// critical, a promise about to go overdue - break through politely ("Forgive the interruption, Sir").
// Nothing breaks into a live call. "That wasn't important" straight after a break-through moves that kind of line down
// to routine for good; "that was important" moves it back.
public partial class MainViewModel
{
    private static string UrgentSpeechPath => Path.Combine(DataFolder, "urgent-speech.json");
    private static readonly TimeSpan BreakThroughFeedbackWindow = TimeSpan.FromMinutes(10);
    private Dictionary<string, bool>? _urgentDowngraded;
    private Dictionary<string, bool> UrgentDowngraded => _urgentDowngraded ??= LoadJson<Dictionary<string, bool>>(UrgentSpeechPath) ?? [];
    private string? _speakingUrgentKind;
    private bool _breakingThrough;
    private (string Kind, DateTimeOffset At)? _lastBreakThrough;

    /// <summary>Speaks a line that may break through quiet, meetings and Do Not Disturb (never a call).</summary>
    private async Task SpeakUrgentAsync(string kind, string message, bool interrupt = true)
    {
        if (UrgentDowngraded.TryGetValue(kind, out var down) && down) { await SpeakGuardianAsync(message); return; }
        // The quiet-mode gate runs synchronously at the start of SpeakGuardianAsync, so the flag only needs to be set
        // for that moment; clearing it straight after keeps other lines spoken meanwhile from riding on it.
        _speakingUrgentKind = kind;
        Task speaking;
        try { speaking = SpeakGuardianAsync(message, interrupt); }
        finally { _speakingUrgentKind = null; }
        await speaking;
    }

    /// <summary>Called by the quiet-mode gate: true when this urgent line may go ahead despite the hold.</summary>
    private bool UrgentBreaksThrough(string holdReason)
    {
        if (_speakingUrgentKind is not { } kind || LiveCallReason() is not null || IsOnPhoneCall) return false;
        _breakingThrough = true;
        _lastBreakThrough = (kind, DateTimeOffset.Now);
        Think($"Interrupting ({holdReason}): this one is urgent");
        return true;
    }

    private string BreakThroughPreface(string message)
    {
        if (!_breakingThrough) return message;
        _breakingThrough = false;
        return (Nekomata.Core.Guardian.FormalManner.Enabled ? "Forgive the interruption, Sir. " : "Sorry to interrupt. ") + message;
    }

    /// <summary>"That wasn't important" / "that was important" right after a break-through: true when handled here.</summary>
    private bool ApplyBreakThroughFeedback(string argument)
    {
        if (argument is not ("less" or "more") || _lastBreakThrough is not { } last || DateTimeOffset.Now - last.At > BreakThroughFeedbackWindow) return false;
        _lastBreakThrough = null;
        if (argument == "less") UrgentDowngraded[last.Kind] = true; else UrgentDowngraded.Remove(last.Kind);
        SaveJson(UrgentSpeechPath, UrgentDowngraded);
        _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "InterruptionFeedback", Category = "Voice", Title = "Interruption feedback", Narrative = $"Break-through: {last.Kind}",
            Outcome = argument, Significance = 35
        });
        var what = last.Kind switch { "urgent" => "urgent-looking items", "due" => "due-date warnings", _ => "that kind of alert" };
        ShowPaletteResponse(argument == "less"
            ? $"Understood. I won't interrupt for {what} again; they'll wait for a natural break. Say \"that was important\" after one to undo it."
            : $"Noted. I'll keep interrupting for {what}.");
        return true;
    }
}
