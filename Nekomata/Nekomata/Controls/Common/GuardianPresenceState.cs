namespace Nekomata.UI.Controls.Common;

/// <summary>
/// Guardian's visible ambient state, shown as a living presence indicator (see
/// <see cref="GuardianPresenceOrb"/>) rather than a static "is busy" flag.
/// </summary>
public enum GuardianPresenceState
{
    /// <summary>Nothing in flight; a slow ambient breathing glow.</summary>
    Idle,

    /// <summary>Capturing spoken input (command-palette dictation).</summary>
    Listening,

    /// <summary>Awaiting a Guardian response (local intent handling or the AI call).</summary>
    Thinking,

    /// <summary>Guardian is speaking the response aloud.</summary>
    Speaking,

    /// <summary>A brief acknowledgement flash for an action Guardian just took on its own
    /// AutoExecute authority, distinct from Thinking/Speaking so it reads as "already
    /// handled" rather than "working on it".</summary>
    Acting
}
