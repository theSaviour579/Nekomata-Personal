using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Context;

namespace Nekomata.UI.ViewModels;

// Teams presence for quiet mode. Calls and calendar meetings are already detected; this covers an unscheduled
// Teams call, presenting / sharing a screen, and Do Not Disturb. Only states that mean live audio, a shared screen
// or an explicit "don't disturb" count: Teams also reports "In a meeting" for any busy calendar slot (including
// the user's own blocks such as a daily wrap-up), and calendar meetings are judged by the calendar rules instead.
// Read with the desktop's delegated sign-in (Presence.Read, already among its scopes). Failures back off for a
// while and simply leave the other quiet rules in charge.
public partial class MainViewModel
{
    private static readonly TimeSpan TeamsPresencePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TeamsPresenceFailureBackoff = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _teamsPresenceTimer = new() { Interval = TeamsPresencePollInterval };
    private string? _teamsBusyReason;
    private DateTimeOffset _teamsPresenceRetryAt;
    private bool _teamsPresenceBusy;

    private void InitialiseTeamsPresence()
    {
        _teamsPresenceTimer.Tick += (_, _) => _ = RefreshTeamsPresenceAsync();
        _teamsPresenceTimer.Start();
    }

    private async Task RefreshTeamsPresenceAsync()
    {
        if (_teamsPresenceBusy || DateTimeOffset.Now < _teamsPresenceRetryAt || IsInitialLoading) return;
        _teamsPresenceBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var presence = await _services.GetRequiredService<IGraphContextService>().GetPresenceAsync(timeout.Token);
            var reason = TeamsQuietReason(presence);
            var changed = !string.Equals(reason, _teamsBusyReason, StringComparison.Ordinal);
            var startedBusy = reason is not null && _teamsBusyReason is null;
            _teamsBusyReason = reason;
            // Same as a call starting to ring: stop mid-sentence rather than talk into a live call or a demo.
            // (Not for Do Not Disturb: that's a preference, not live audio, so the current sentence can finish.)
            if (startedBusy && reason != TeamsDoNotDisturb && _guardianSpeakingDepth > 0)
                _guardianSpeech.StopSpeaking();
            if (changed) await EvaluateQuietStateAsync();
        }
        catch (Exception ex)
        {
            _teamsBusyReason = null;
            _teamsPresenceRetryAt = DateTimeOffset.Now + TeamsPresenceFailureBackoff;
            System.Diagnostics.Debug.WriteLine($"Teams presence unavailable; retrying in {TeamsPresenceFailureBackoff.TotalMinutes} min: {ex.Message}");
        }
        finally
        {
            _teamsPresenceBusy = false;
        }
    }

    private const string TeamsDoNotDisturb = "on Do Not Disturb";

    private static string? TeamsQuietReason(GraphPresence presence) => presence.Activity switch
    {
        "InACall" or "InAConferenceCall" => "on a Teams call",
        "Presenting" => "presenting",
        _ => presence.Availability.Equals("DoNotDisturb", StringComparison.OrdinalIgnoreCase) ? TeamsDoNotDisturb : null
    };
}
