using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.UI.Controls.Common;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Guardian's ambient presence state, bound to the header's GuardianPresenceOrb.
    /// Set from the command/voice/chat code paths that already track busy/listening/
    /// speaking state (SendGuardianMessageAsync, DictateCommandPaletteAsync,
    /// SpeakGuardianAsync) rather than introduced as new state machinery.
    /// </summary>
    [ObservableProperty]
    private GuardianPresenceState guardianPresenceState = GuardianPresenceState.Idle;

    /// <summary>Tracks overlapping SpeakGuardianAsync calls so the state only drops
    /// back to Idle once the last one finishes (several call sites fire-and-forget
    /// speech while another may already be queued behind GuardianSpeechService's
    /// own gate).</summary>
    private int _guardianSpeakingDepth;
}
