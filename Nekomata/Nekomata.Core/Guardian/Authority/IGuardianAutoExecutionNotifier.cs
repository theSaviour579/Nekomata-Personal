using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Authority;

/// <summary>
/// Lets a UI surface (the desktop orb and voice, today) acknowledge an action Guardian just
/// took on its own AutoExecute authority, rather than leaving it visible only in the audit
/// log afterwards. Optional and UI-agnostic: GuardianActionAuthorityEngine calls this when
/// registered, and nothing changes when it isn't (the test suite never
/// register one).
/// </summary>
public interface IGuardianAutoExecutionNotifier
{
    Task NotifyAutoExecutedAsync(GuardianActionAuthorityDecision decision, GuardianActionAuthorityRequest request);
}
