using Nekomata.UI.Controls.Common;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Called by DesktopGuardianAutoExecutionNotifier whenever Guardian takes an action under
    /// its own AutoExecute authority (GuardianActionAuthorityEngine.EvaluateAndRecordAsync),
    /// so it is acknowledged live rather than only visible later in the audit log. Flashes
    /// the orb amber and, when voice is enabled, speaks a short acknowledgement.
    /// </summary>
    public async Task AcknowledgeAutoExecutedActionAsync(string actionSummary)
    {
        if (string.IsNullOrWhiteSpace(actionSummary)) return;
        RecordDone($"Took care of {actionSummary}.");

        GuardianPresenceState = GuardianPresenceState.Acting;
        try
        {
            await Task.Delay(420);
            if (GuardianVoiceEnabled)
                await SpeakGuardianAsync($"Already taken care of — {actionSummary}.");
            else
                await Task.Delay(1100);
        }
        finally
        {
            // Always return to Idle. Speech may be held (on a call, in a meeting, quiet mode) or skipped as a
            // repeat, in which case SpeakGuardianAsync never moves the orb through Speaking -> Idle, and the
            // orb would otherwise stay amber and still indefinitely.
            if (GuardianPresenceState == GuardianPresenceState.Acting)
                GuardianPresenceState = GuardianPresenceState.Idle;
        }
    }
}
