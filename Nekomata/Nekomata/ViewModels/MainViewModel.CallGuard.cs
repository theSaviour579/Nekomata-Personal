using System.Windows.Threading;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Never talk into a live call. Checked every two seconds: another app holding the microphone (a Teams, Zoom or browser
// call; see CallDetector), or Teams presence "in a call". While either is true:
//   * Guardian stops mid-sentence and says nothing aloud, even an answer the user asked for (that stays on screen);
//   * unprompted lines are held and recapped after the call, as quiet mode already does;
//   * it doesn't keep listening for follow-ups or barge-in, so the user's half of the call isn't taken as commands.
// Pressing the talk key during a call still works (the user chose to), but the answer is shown, not spoken.
public partial class MainViewModel
{
    private readonly DispatcherTimer _callGuardTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _micInUseBy;

    private void InitialiseCallGuard()
    {
        _callGuardTimer.Tick += (_, _) => CheckForLiveCall();
        _callGuardTimer.Start();
    }

    private void CheckForLiveCall()
    {
        // Our own listening holds the mic too, but CallDetector ignores Nekomata.
        var app = CallDetector.OtherAppUsingMicrophone();
        if (app == _micInUseBy) return;
        var started = app is not null && _micInUseBy is null;
        var ended = app is null && _micInUseBy is not null;
        _micInUseBy = app;
        if (started) _ = PauseMusicForCallAsync();
        if (ended) _ = ResumeMusicAfterCallAsync();
        if (started)
        {
            if (_guardianSpeakingDepth > 0) _guardianSpeech.StopSpeaking();
            _bargeInCts?.Cancel();
            if (_conversationActive) _listenCts?.Cancel();
            Think($"You're on a call in {app}. I'll stay quiet and keep anything for after");
        }
        _ = EvaluateQuietStateAsync();
    }

    /// <summary>Why Guardian must not speak at all (a live call), or null.</summary>
    private string? LiveCallReason()
    {
        if (_micInUseBy is { } app) return $"on a call in {app}";
        if (_teamsBusyReason == "on a Teams call") return _teamsBusyReason;
        return null;
    }
}
