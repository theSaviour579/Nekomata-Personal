using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Guardian.Evidence;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Decisions;

public class GuardianDecisionEngine
    : IGuardianDecisionEngine
{
    private readonly GuardianReasonBuilder _reasonBuilder;

    private readonly GuardianRiskBuilder _riskBuilder;

    private readonly GuardianOpportunityBuilder _opportunityBuilder;

    private readonly GuardianConfidenceBuilder _confidenceBuilder;

    private readonly GuardianNarrativeBuilder _narrativeBuilder;

    private readonly IGuardianEvidenceBuilder _evidenceBuilder;

    public GuardianDecisionEngine(
    IGuardianEvidenceBuilder evidenceBuilder,
    GuardianReasonBuilder reasonBuilder,
    GuardianRiskBuilder riskBuilder,
    GuardianOpportunityBuilder opportunityBuilder,
    GuardianConfidenceBuilder confidenceBuilder,
    GuardianNarrativeBuilder narrativeBuilder)
    {
        _evidenceBuilder = evidenceBuilder;

        _reasonBuilder = reasonBuilder;
        _riskBuilder = riskBuilder;
        _opportunityBuilder = opportunityBuilder;
        _confidenceBuilder = confidenceBuilder;
        _narrativeBuilder = narrativeBuilder;
    }
    public GuardianDecision Analyse(
    NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var decision =
            new GuardianDecision();

        decision.Reasons.Clear();
        decision.Risks.Clear();
        decision.Opportunities.Clear();

        var evidence =
            _evidenceBuilder.Build(workspace);

        workspace.GuardianEvidence =
            evidence;

        decision.Reasons.AddRange(
            _reasonBuilder.Build(evidence));

        decision.Risks.AddRange(
            _riskBuilder.Build(evidence));

        decision.Opportunities.AddRange(
            _opportunityBuilder.Build(evidence));

        System.Diagnostics.Debug.WriteLine(
            $"Decision risks: {decision.Risks.Count}");

        foreach (var risk in decision.Risks)
        {
            System.Diagnostics.Debug.WriteLine(
                $"RISK: {risk.Title} | " +
                $"Critical={risk.Critical} | " +
                $"{risk.Description}");
        }

        _narrativeBuilder.Apply(
            workspace,
            decision);

        decision.Confidence =
            _confidenceBuilder.Calculate(
                workspace,
                decision.Reasons);

        return decision;
    }
}