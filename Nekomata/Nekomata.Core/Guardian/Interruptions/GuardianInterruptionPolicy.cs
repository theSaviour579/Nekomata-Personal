using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Interruptions;

public sealed class GuardianInterruptionPolicy
{
    public GuardianInterruptionDecision Evaluate(GuardianInterruptionSignal signal, DateTimeOffset now)
    {
        var text = $"{signal.Source} {signal.Title} {signal.Detail}".ToLowerInvariant();
        var evidence = new List<string>();
        var score = SourceWeight(signal.Source);
        if (score > 0) evidence.Add($"{signal.Source} source weight +{score}.");

        var severity = signal.Severity.ToLowerInvariant() switch
        {
            "critical" or "urgent" => 60,
            "high" => 45,
            "warning" => 25,
            _ => 5
        };
        score += severity; evidence.Add($"{signal.Severity} severity +{severity}.");

        var incident = ContainsAny(text, "outage", "security", "breach", "p1", "cannot login", "can't login", "blocked", "blocks ", "entra");
        if (incident)
        { score += 40; evidence.Add("Blocking or incident language +40."); }
        if (ContainsAny(text, "today", "right now", "immediately", "urgent"))
        { score += 15; evidence.Add("Explicit timing language +15."); }
        if (ContainsAny(text, "overdue", "breached", "deadline", "due now", "missed"))
        { score += 25; evidence.Add("Deadline consequence +25."); }
        if (ContainsAny(text, "craig", "phil", "executive", "director", "management"))
        { score += 20; evidence.Add("Senior stakeholder context +20."); }
        if (signal.ActionKind.StartsWith("review_", StringComparison.OrdinalIgnoreCase) ||
            signal.ActionKind.Contains("approve", StringComparison.OrdinalIgnoreCase) ||
            signal.ActionKind.Contains("resolve_conflict", StringComparison.OrdinalIgnoreCase))
        { score += 15; evidence.Add("Human decision required +15."); }
        if (signal.Deadline is { } deadline)
        {
            var minutes = (deadline - now).TotalMinutes;
            if (minutes <= 30) { score += 35; evidence.Add("Deadline within 30 minutes +35."); }
            else if (minutes <= 120) { score += 15; evidence.Add("Deadline within two hours +15."); }
        }

        var adjustment = signal.PreferenceAdjustment;
        if (adjustment < 0 && (severity >= 60 || incident))
        {
            evidence.Add("Your preference to see less of this kind was set aside: critical or incident evidence always counts.");
            adjustment = 0;
        }
        else if (adjustment != 0)
        {
            score += adjustment;
            evidence.Add($"Your preference for this kind of item {(adjustment > 0 ? "+" : "")}{adjustment}.");
        }

        var busy = signal.FocusActive || signal.CalendarCommitmentActive;
        if (busy) evidence.Add($"Interruption cost: protected {(signal.FocusActive ? "focus" : "calendar commitment")} is active.");
        var routine = ContainsAny(text, "automatic acknowledgement", "auto acknowledgement", "routine sync", "healthy", "no action required", "duplicate");
        if (routine && severity < 45)
            return Decide(GuardianInterruptionRoute.Suppress, Math.Max(0, score - 60), 94,
                "Routine, duplicate or acknowledgement evidence does not justify interruption.", evidence);

        // High severity normally interrupts outright, unless the user has said this kind isn't worth it: then it has to
        // earn its place on score like everything else. Critical always interrupts.
        if (severity >= 60 || (severity >= 45 && adjustment >= 0) || score >= 70)
            return Decide(GuardianInterruptionRoute.Immediate, score, 90,
                "Urgency or consequence outweighs the disruption cost; surface this now.", evidence);
        if (busy && (score >= 25 || signal.ExplicitlyRaisedByUser))
            return Decide(GuardianInterruptionRoute.NaturalBreak, score, 84,
                $"Protect {SafeObjective(signal.CurrentObjective)} and release this at the next natural break.", evidence);
        if (!busy && score >= 45)
            return Decide(GuardianInterruptionRoute.Immediate, score, 82,
                "No protected work is active and the signal warrants review now.", evidence);
        if (score >= 20 || signal.ExplicitlyRaisedByUser)
            return Decide(GuardianInterruptionRoute.Briefing, score, 78,
                "Retain this for the next contextual briefing without interrupting current work.", evidence);
        return Decide(GuardianInterruptionRoute.Suppress, score, 86,
            "The signal lacks sufficient urgency, consequence or required action.", evidence);
    }

    private static GuardianInterruptionDecision Decide(GuardianInterruptionRoute route, int score, int confidence,
        string explanation, IReadOnlyList<string> evidence) => new(route, Math.Clamp(score, 0, 100), confidence, explanation, evidence);
    private static int SourceWeight(string source) => source.ToLowerInvariant() switch
    {
        var x when x.Contains("calendar") => 20,
        var x when x.Contains("forecast") => 25,
        var x when x.Contains("project") => 20,
        var x when x.Contains("email") => 10,
        var x when x.Contains("teams") => 15,
        var x when x.Contains("task") => 15,
        var x when x.Contains("manual") => 15,
        _ => 0
    };
    private static string SafeObjective(string value) => string.IsNullOrWhiteSpace(value) ? "the current commitment" : value;
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(value.Contains);
}
