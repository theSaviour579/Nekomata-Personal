using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

// Focus guard debrief. While a mission runs, GuardianInterruptionPolicy holds back
// non-urgent items (NaturalBreak), parks lower-value ones for the next briefing
// (Briefing) and filters routine noise (Suppress). When the mission ends, Guardian says
// what happened in one short handover instead of the held items reappearing silently:
// how many came in, the most important one or two, what was parked, what was filtered.
// It stays quiet when a meeting is running (ReleaseQueuedInterruptions returns nothing
// then) and when the focus session was too short to be worth reporting on.
public partial class MainViewModel
{
    private static readonly TimeSpan FocusDebriefMinimumSession = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan QuietFocusMentionThreshold = TimeSpan.FromMinutes(25);

    private DateTimeOffset? _focusStartedAt;
    private int _focusFilteredCount;

    private void BeginFocusDebriefWindow()
    {
        _focusStartedAt = DateTimeOffset.Now;
        _focusFilteredCount = 0;
    }

    private async Task SpeakFocusDebriefAsync(IReadOnlyList<GuardianAttentionItem> released)
    {
        var startedAt = _focusStartedAt;
        var filtered = _focusFilteredCount;
        _focusStartedAt = null;
        _focusFilteredCount = 0;
        if (startedAt is null) return;

        var now = DateTimeOffset.Now;
        var session = now - startedAt.Value;
        if (session < FocusDebriefMinimumSession) return;
        if (CalendarEvents.Any(x => !x.IsAllDay && x.Start <= now && x.End > now)) return;

        var parked = AttentionItems.Count(x => x.IsActive &&
                                               x.InterruptionRoute == GuardianInterruptionRoute.Briefing &&
                                               x.InterruptionReleasedAt is null &&
                                               x.CreatedAt >= startedAt.Value);
        var message = ComposeFocusDebrief(released, parked, filtered, session);
        if (message is null) return;

        try
        {
            // Let the mission-completion line (if any) go first; speech is queued, not interrupted.
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            if (_conversationActive) return;
            GuardianResponse = message;
            await SpeakGuardianAsync(message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Focus debrief failed safely: {ex}");
        }
    }

    private static string? ComposeFocusDebrief(IReadOnlyList<GuardianAttentionItem> released, int parked, int filtered, TimeSpan session)
    {
        if (released.Count == 0 && parked == 0 && filtered == 0)
            return session >= QuietFocusMentionThreshold
                ? $"Good session. Nothing came in during your {SpokenDuration(session)} of focus."
                : null;

        var parts = new List<string>();
        if (released.Count > 0)
        {
            var ordered = released
                .OrderByDescending(x => SeverityRank(x.Severity))
                .ThenByDescending(x => x.InterruptionPriorityScore)
                .ThenByDescending(x => x.CreatedAt)
                .ToList();
            parts.Add(released.Count == 1
                ? $"While you were focused, one thing came in, nothing urgent: {SpokenTitle(ordered[0].Title)}."
                : $"While you were focused, {released.Count} things came in, nothing urgent. Most important: {SpokenTitle(ordered[0].Title)}.");
            if (ordered.Count > 1) parts.Add($"Then {SpokenTitle(ordered[1].Title)}.");
        }
        else
            parts.Add("Nothing needed you while you were focused.");

        if (parked > 0)
            parts.Add(parked == 1
                ? "One lower-priority item is saved for your next briefing."
                : $"{parked} lower-priority items are saved for your next briefing.");
        if (filtered > 0)
            parts.Add(filtered == 1
                ? "I filtered out one routine notification."
                : $"I filtered out {filtered} routine notifications.");
        return string.Join(" ", parts);
    }

    private static string SpokenDuration(TimeSpan span)
    {
        var minutes = (int)Math.Round(span.TotalMinutes);
        if (minutes < 60) return $"{minutes} minutes";
        var hours = minutes / 60;
        var rest = minutes % 60;
        var hourText = hours == 1 ? "an hour" : $"{hours} hours";
        return rest < 5 ? hourText : $"{hourText} and {rest} minutes";
    }
}
