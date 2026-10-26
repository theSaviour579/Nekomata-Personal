using Nekomata.Core.Analytics;

namespace Nekomata.Core.Planning;

public enum RoleCorrectionType { ReserveStrategicBlock, PreserveLeadershipCommitment, DelegateRecurringWork, ProtectOversightBlock, ReduceAdministration }
public enum RoleCorrectionDecision { Proposed, Approved, Rejected }

public sealed record RoleCorrectiveAction(
    Guid Id, RoleCorrectionType Type, string BandKey, string Title, int Minutes,
    int PriorityScore, double ExpectedAlignmentImprovement, int BiasStrength,
    string Reason, IReadOnlyList<string> Evidence, ProtectedBlockRisk? SuggestedBlock = null,
    RoleCorrectionDecision Decision = RoleCorrectionDecision.Proposed)
{
    public string EstimateLabel => $"Estimated +{ExpectedAlignmentImprovement:0.#} alignment points";
    public bool RequiresApproval => true;
}

public sealed record RoleCorrectivePlan(
    IReadOnlyList<RoleCorrectiveAction> RankedActions, int AvailableCapacityMinutes,
    int RecommendedMinutes, double ExpectedAlignmentImprovement, string Rationale)
{
    public RoleCorrectiveAction? MostImportant => RankedActions.FirstOrDefault();
}

public sealed class CorrectivePlanEngine
{
    public RoleCorrectivePlan Build(
        RoleAlignmentSummary alignment, IReadOnlyList<RoleBias> biases,
        int availableCapacityMinutes, int overtimeMinutes, int alreadyProtectedMinutes,
        ProtectedBlockRisk? preferredBlock, double interruptionRiskPercent,
        IReadOnlyDictionary<RoleCorrectionType, double>? measuredEffectiveness = null)
    {
        var candidates = new List<RoleCorrectiveAction>();
        foreach (var bias in biases.Where(item => item.Strength > 0 && item.Direction != "Decaying"))
        {
            var action = CreateAction(bias, alignment, preferredBlock);
            if (action is not null) candidates.Add(action);
        }

        var capacityFactor = availableCapacityMinutes <= 0 ? .35 : availableCapacityMinutes < 90 ? .65 : 1;
        var overtimeFactor = overtimeMinutes >= 120 ? 1.2 : overtimeMinutes >= 30 ? 1.1 : 1;
        var protectedPenalty = alreadyProtectedMinutes >= 180 ? 12 : alreadyProtectedMinutes >= 90 ? 6 : 0;
        var riskPenalty = interruptionRiskPercent >= 60 ? 12 : interruptionRiskPercent >= 40 ? 6 : 0;
        candidates = candidates.Select(action => action with
        {
            PriorityScore = Math.Clamp((int)Math.Round(action.PriorityScore *
                (action.Type is RoleCorrectionType.DelegateRecurringWork or RoleCorrectionType.ReduceAdministration ? 1 : capacityFactor) * overtimeFactor *
                (.8 + .4 * (measuredEffectiveness?.GetValueOrDefault(action.Type, .5) ?? .5))) -
                (action.SuggestedBlock is null ? 0 : protectedPenalty + riskPenalty), 1, 100),
            Evidence = action.Evidence.Concat([
                $"Available capacity {availableCapacityMinutes}m; current overtime {overtimeMinutes}m; already protected {alreadyProtectedMinutes}m.",
                $"Estimated interruption risk {interruptionRiskPercent:0}%.",
                measuredEffectiveness?.ContainsKey(action.Type) == true
                    ? $"Measured historical effectiveness for this intervention: {measuredEffectiveness[action.Type]:P0}."
                    : "No measured intervention outcome exists yet; a neutral effectiveness prior is used."]).ToList()
        }).OrderByDescending(action => action.PriorityScore).ThenByDescending(action => action.ExpectedAlignmentImprovement).ToList();

        // Minimum viable correction: take the smallest ranked set that can add roughly
        // five alignment points, with a hard ceiling of three interventions and 180 minutes.
        var selected = new List<RoleCorrectiveAction>();
        var minutes = 0;
        var improvement = 0d;
        var interventionBudget = Math.Min(180, Math.Max(30, availableCapacityMinutes));
        foreach (var action in candidates)
        {
            if (selected.Count >= 3 || minutes + action.Minutes > interventionBudget) continue;
            selected.Add(action); minutes += action.Minutes; improvement += action.ExpectedAlignmentImprovement;
            if (improvement >= 5) break;
        }
        if (selected.Count == 0 && candidates.FirstOrDefault(action => action.Minutes <= interventionBudget) is { } fallback)
            selected.Add(fallback);
        minutes = selected.Sum(item => item.Minutes);
        improvement = selected.Sum(item => item.ExpectedAlignmentImprovement);
        return new(selected, availableCapacityMinutes, minutes, Math.Round(improvement, 1),
            selected.Count == 0
                ? "No correction is justified by the available evidence and capacity."
                : $"Selected {selected.Count} minimum viable intervention(s) rather than attempting to force every band to target.");
    }

    private static RoleCorrectiveAction? CreateAction(RoleBias bias, RoleAlignmentSummary alignment, ProtectedBlockRisk? block)
    {
        var strategicWeight = bias.BandKey == "strategic" ? 1.25 : bias.BandKey == "leadership" ? 1.15 : 1;
        var expected = Math.Clamp(bias.CurrentDeviationPoints * .35 * strategicWeight, 1, 8);
        var priority = Math.Clamp((int)Math.Round(bias.Strength * .65 + bias.CurrentDeviationPoints * strategicWeight * 2), 1, 100);
        return (bias.BandKey, bias.Direction) switch
        {
            ("strategic", "Underweight") => Action(RoleCorrectionType.ReserveStrategicBlock, "Reserve one 90-minute strategic block", 90, block),
            ("leadership", "Underweight") => Action(RoleCorrectionType.PreserveLeadershipCommitment, "Preserve one leadership, coaching or 1:1 commitment", 60, block),
            ("oversight", "Underweight") => Action(RoleCorrectionType.ProtectOversightBlock, "Protect one operational-oversight review", 45, block),
            ("technical", "Overweight") or ("reactive", "Overweight") => Action(RoleCorrectionType.DelegateRecurringWork, "Prepare two suitable recurring requests for delegation", 30, null),
            ("admin", "Overweight") => Action(RoleCorrectionType.ReduceAdministration, "Batch or remove one avoidable administration block", 30, null),
            _ => null
        };

        RoleCorrectiveAction Action(RoleCorrectionType type, string title, int minutes, ProtectedBlockRisk? suggested) =>
            new(Guid.NewGuid(), type, bias.BandKey, title, minutes, priority, Math.Round(expected, 1), bias.Strength,
                bias.MainReason, bias.Evidence, suggested);
    }
}
