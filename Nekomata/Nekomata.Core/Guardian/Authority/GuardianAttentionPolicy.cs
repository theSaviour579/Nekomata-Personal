namespace Nekomata.Core.Guardian.Authority;

public sealed record GuardianAttentionAssessment(bool RequiresJudgement, string Reason);

public static class GuardianAttentionPolicy
{
    private static readonly string[] JudgementActions =
    [
        "adaptive_replan", "review_project_hold", "review_assignment",
        "approve_action", "resolve_conflict", "spotify_arrival_swap"
    ];
    private static readonly string[] JudgementTerms =
    [
        "ambiguous", "conflict", "breach", "failed", "failure", "rejected", "suspicious",
        "approval", "judgement", "overdue", "missed", "abnormal", "outside normal", "at risk"
    ];

    public static GuardianAttentionAssessment Assess(string source, string severity, string title,
        string detail, string actionKind)
    {
        if (severity.Equals("Critical", StringComparison.OrdinalIgnoreCase) ||
            severity.Equals("High", StringComparison.OrdinalIgnoreCase))
            return new(true, "Elevated impact requires human awareness or judgement.");
        if (JudgementActions.Contains(actionKind, StringComparer.OrdinalIgnoreCase))
            return new(true, "The proposed action requires a human decision.");
        var text = $"{title} {detail}";
        if (JudgementTerms.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)))
            return new(true, "The evidence contains an exception, conflict or material uncertainty.");
        if (source.Contains("PROJECT UPDATE", StringComparison.OrdinalIgnoreCase))
            return new(true, "A response window has elapsed and the next step needs review.");
        return new(false, "This is routine or healthy work and belongs in Missions, Projects or Chronicle.");
    }
}
