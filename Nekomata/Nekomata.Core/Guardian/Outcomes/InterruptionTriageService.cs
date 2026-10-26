using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Core.Guardian.Interruptions;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class InterruptionTriageService(GuardianInterruptionPolicy? policy = null)
{
    private readonly GuardianInterruptionPolicy _policy = policy ?? new();

    public InterruptionTriage Triage(string description, Mission mission, int remainingMinutes)
    {
        var text = description.Trim().ToLowerInvariant();
        if (ContainsAny(text, "delegate", "for alex", "alex can", "pass to alex", "assign to alex"))
            return new("Delegated", "DELEGATE",
                $"This appears suitable for delegation. Preserve {mission.Title} and create a follow-up for Alex.", 78);

        var decision = _policy.Evaluate(new()
        {
            Source = "Manual interruption", Severity = "Info", Title = description, Detail = description,
            ActionKind = "review_interruption", FocusActive = true, ExplicitlyRaisedByUser = true,
            CurrentObjective = mission.Title, RemainingFocusMinutes = remainingMinutes
        }, DateTimeOffset.Now);
        return decision.Route == GuardianInterruptionRoute.Immediate
            ? new("Urgent", "URGENT — SWITCH", $"{decision.Explanation} {mission.Title} is paused with {remainingMinutes} mins remaining.", decision.ConfidencePercent)
            : new("Queued", "QUEUE — DON'T SWITCH", $"{decision.Explanation} Continue {mission.Title}; {remainingMinutes} mins remain.", decision.ConfidencePercent);
    }

    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);
}
