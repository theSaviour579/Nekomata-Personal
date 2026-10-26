using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class GuardianPolicyAccountabilityService(IGuardianPolicyRepository repository)
{
    public async Task<List<GuardianPolicy>> ReconcileAsync(IEnumerable<GuardianOutcomeInsight> insights)
    {
        foreach (var insight in insights.Where(x => !string.IsNullOrWhiteSpace(x.PolicyAction)))
        {
            var key = $"{insight.PolicyAction}:{insight.WorkType}".ToLowerInvariant();
            await repository.UpsertEvidenceAsync(new GuardianPolicy
            {
                Id = DeterministicId(key), PolicyKey = key, Name = FriendlyName(insight.PolicyAction, insight.WorkType),
                Statement = insight.Summary, Action = insight.PolicyAction, WorkType = insight.WorkType,
                Lifecycle = insight.IsPolicyActive ? "Active" : "Candidate",
                ConfidencePercent = insight.ConfidencePercent, EvidenceCount = insight.SampleSize
            });
        }
        var policies = await repository.GetAllAsync();
        foreach (var policy in policies.Where(x => x.Lifecycle == "Active" && x.AppliedCount >= 5 && x.AdverseCount * 100 / x.AppliedCount >= 35))
            await repository.SetLifecycleAsync(policy.Id, "Challenged");
        return await repository.GetAllAsync();
    }

    public Task RecordAppliedAsync(Mission mission, bool counterfactual = false) =>
        repository.AddApplicationsAsync(mission.AppliedPolicyIds, mission.TaskId, mission.ProjectId, counterfactual);

    /// <summary>
    /// Records that the per-task duration-calibration policy (identified by <see
    /// cref="ComputePolicyId"/>, computed from the same action+work-type key ReconcileAsync
    /// upserts the policy row under) actually overrode a task's derived estimate, so the
    /// policy's "applied to N decisions" count reflects real corrections instead of staying
    /// at zero. GuardianOutcomeEngine re-evaluates every task on each refresh, so this is kept
    /// idempotent per (policy, task) rather than inserting a fresh row every time.
    /// </summary>
    public async Task RecordDurationCorrectionAppliedAsync(Guid policyId, long taskId, long? projectId)
    {
        if (await repository.HasApplicationAsync(policyId, taskId)) return;
        await repository.AddApplicationsAsync([policyId], taskId, projectId, counterfactual: false);
    }

    public Task RecordOutcomeAsync(Mission mission, string outcome, int strategicMinutes) =>
        repository.CompleteApplicationsAsync(mission.AppliedPolicyIds, mission.TaskId, mission.ProjectId, outcome, strategicMinutes);

    public Task SetLifecycleAsync(Guid id, string lifecycle) => repository.SetLifecycleAsync(id, lifecycle);

    /// <summary>The same policy-key hash ReconcileAsync/UpsertEvidenceAsync uses, exposed so
    /// callers that apply a policy outside the Mission/AppliedPolicyIds pathway (such as
    /// GuardianOutcomeEngine's per-task duration calibration) can record against the correct
    /// policy id without duplicating the hashing scheme.</summary>
    public static Guid ComputePolicyId(string action, string workType) =>
        DeterministicId($"{action}:{workType}".ToLowerInvariant());

    private static string FriendlyName(string action, string workType) => action switch
    {
        "prefer_analysis_before_11" => "Morning Analysis Policy",
        "prefer_deep_morning" => "Morning Deep Work Policy",
        "avoid_deep_afternoon" => "Afternoon Interruptibility Policy",
        "protect_morning_strategic_block" => "Strategic Capacity Protection Policy",
        "apply_duration_correction" => $"{workType} Estimation Calibration Policy",
        _ => "Guardian Planning Policy"
    };

    private static Guid DeterministicId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }
}
