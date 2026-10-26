using Nekomata.Core.Analytics;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Planning;

public sealed record RolePlanningPlan(
    DateTime GeneratedAt, int AlignmentScore, bool ReserveStrategicBlock,
    bool ProtectLeadershipTime, bool IncreaseDelegationPressure,
    IReadOnlyList<string> Actions)
{
    public IReadOnlyList<RoleBias> ActiveBiases { get; init; } = [];
    public RoleCorrectivePlan? CorrectivePlan { get; init; }
    public RoleAlignmentForecast? Forecast { get; init; }
}

public sealed class RolePlanningPolicy
{
    public RolePlanningPlan Create(RoleAlignmentSummary alignment, DateTime now)
        => Create(alignment, now, [], null, null);

    public RolePlanningPlan Create(
        RoleAlignmentSummary alignment, DateTime now, IReadOnlyList<RoleBias> biases,
        RoleCorrectivePlan? correctivePlan, RoleAlignmentForecast? forecast)
    {
        if (!alignment.HasEvidence)
            return new RolePlanningPlan(now, 0, false, false, false,
                ["Role planning is observing until enough recorded work exists to justify changing recommendations."])
                { ActiveBiases = biases, CorrectivePlan = correctivePlan, Forecast = forecast };

        var strategic = alignment.Bands.Single(band => band.Key == "strategic");
        var technical = alignment.Bands.Single(band => band.Key == "technical");
        var reactive = alignment.Bands.Single(band => band.Key == "reactive");
        var reserveStrategic = strategic.Status == "Below target" || alignment.PersistentUnderweightKeys.Contains("strategic");
        var protectLeadership = alignment.PersistentUnderweightKeys.Contains("leadership");
        var increaseDelegation = technical.Status == "Above target" || reactive.Status == "Above target" ||
                                 alignment.PersistentOverweightKeys.Contains("technical") || alignment.PersistentOverweightKeys.Contains("reactive");
        var actions = new List<string>();
        if (reserveStrategic)
            actions.Add($"Reserve a 90-minute high-leverage block; strategic / transformation is {strategic.ActualPercent:0.#}% against a {strategic.TargetLabel} target.");
        if (protectLeadership)
            actions.Add("Preserve coaching, 1:1 and project-review commitments; leadership has been below band for three recorded weeks.");
        if (increaseDelegation)
            actions.Add("Challenge suitable technical or reactive work for delegation before allowing it to displace manager-level work.");
        if (actions.Count == 0) actions.Add("Keep the normal plan: no role-correction bias is justified by current evidence.");
        return new RolePlanningPlan(now, alignment.Score, reserveStrategic, protectLeadership, increaseDelegation, actions)
            { ActiveBiases = biases, CorrectivePlan = correctivePlan, Forecast = forecast };
    }

    public IReadOnlyList<MissionCandidate> Apply(RolePlanningPlan plan, IEnumerable<MissionCandidate> candidates)
    {
        var list = candidates.ToList();
        foreach (var candidate in list)
        {
            candidate.Score -= candidate.RoleAlignmentScoreAdjustment;
            candidate.ScoreFactors.RemoveAll(factor => factor.Category == "Role Alignment");
            if (candidate.RoleAlignmentRecommendationSuffix.Length > 0 &&
                candidate.RecommendationReason.EndsWith(candidate.RoleAlignmentRecommendationSuffix, StringComparison.Ordinal))
                candidate.RecommendationReason = candidate.RecommendationReason[..^candidate.RoleAlignmentRecommendationSuffix.Length];
            candidate.RoleAlignmentScoreAdjustment = 0;
            candidate.RoleAlignmentRecommendationSuffix = "";
            if (candidate.IsUrgent || candidate.RequiresImmediateAttention) continue;

            var adjustment = 0;
            var explanations = new List<string>();
            if (plan.ReserveStrategicBlock && IsStrategic(candidate))
            {
                adjustment += PositivePoints(plan, "strategic", 12);
                explanations.Add("Strategic work is below its configured role band, so Guardian is protecting recovery capacity.");
            }
            if (plan.ProtectLeadershipTime && IsLeadership(candidate))
            {
                adjustment += PositivePoints(plan, "leadership", 10);
                explanations.Add("Leadership has been underweight for three recorded weeks.");
            }
            if (plan.IncreaseDelegationPressure && IsTechnicalOrReactive(candidate) &&
                candidate.CanReassignOwner && candidate.AssignmentConfidence >= 55)
            {
                adjustment -= PositivePoints(plan, RoleDriftCauseClassifier.ClassifyBand(candidate.Title, candidate.SourceType), 12);
                explanations.Add($"This work is suitable for {candidate.SuggestedOwner} at {candidate.AssignmentConfidence}% assignment confidence; direct delivery is already overweight.");
            }
            if (adjustment == 0) continue;
            candidate.RoleAlignmentScoreAdjustment = adjustment;
            candidate.Score += adjustment;
            candidate.ScoreFactors.Add(new MissionScoreFactor
            {
                Category = "Role Alignment", Name = adjustment > 0 ? "Role Recovery" : "Delegation Pressure",
                Points = adjustment, Explanation = string.Join(" ", explanations)
            });
            candidate.RoleAlignmentRecommendationSuffix = " Role alignment: " + string.Join(" ", explanations);
            candidate.RecommendationReason += candidate.RoleAlignmentRecommendationSuffix;
        }

        var ranked = list.OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.AtRisk)
            .ThenBy(candidate => candidate.DueAt ?? DateTime.MaxValue)
            .ThenByDescending(candidate => candidate.BusinessValue).ToList();
        for (var index = 0; index < ranked.Count; index++) ranked[index].Rank = index + 1;
        return ranked;
    }

    private static bool IsStrategic(MissionCandidate candidate)
    {
        var text = $"{candidate.Title} {candidate.Description}".ToLowerInvariant();
        return candidate.SourceType.Equals("Project", StringComparison.OrdinalIgnoreCase) ||
               candidate.BusinessValue >= 10_000 || candidate.StrategicBusinessValue >= 100_000 ||
               ContainsAny(text, "strateg", "transformation", "automation", "automate", "implementation", "infrastructure", "process improvement");
    }

    private static bool IsLeadership(MissionCandidate candidate)
    {
        var text = $"{candidate.Title} {candidate.Description}".ToLowerInvariant();
        return ContainsAny(text, "1:1", "one to one", "coaching", "mentor", "team review", "project review", "people management", "team leadership");
    }

    private static bool IsTechnicalOrReactive(MissionCandidate candidate)
    {
        var text = $"{candidate.Title} {candidate.Description} {candidate.SourceType}".ToLowerInvariant();
        return ContainsAny(text, "request", "incident", "support", "sql", "report", "data", "technical", "urgent fix");
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
    private static int PositivePoints(RolePlanningPlan plan, string bandKey, int fallback)
    {
        var bias = plan.ActiveBiases.FirstOrDefault(item => item.BandKey == bandKey && item.Direction != "Decaying");
        return bias is null ? fallback : Math.Clamp(2 + (int)Math.Round(bias.Strength * .14), 3, 16);
    }
}
