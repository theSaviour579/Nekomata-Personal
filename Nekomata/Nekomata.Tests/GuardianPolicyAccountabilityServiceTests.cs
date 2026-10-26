using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianPolicyAccountabilityServiceTests
{
    [Fact]
    public async Task Reconcile_PromotesQualifiedFindingAndChallengesAdversePolicy()
    {
        var repository = new FakePolicyRepository();
        var service = new GuardianPolicyAccountabilityService(repository);
        await service.ReconcileAsync([new GuardianOutcomeInsight
        {
            PolicyAction = "prefer_analysis_before_11", WorkType = "Analysis", Summary = "Morning succeeds",
            ConfidencePercent = 84, SampleSize = 23, IsPolicyActive = true
        }]);
        Assert.Equal("Active", repository.Policy!.Lifecycle);

        repository.Policy.AppliedCount = 6;
        repository.Policy.AdverseCount = 3;
        await service.ReconcileAsync([]);
        Assert.Equal("Challenged", repository.Policy.Lifecycle);
    }

    private sealed class FakePolicyRepository : IGuardianPolicyRepository
    {
        public GuardianPolicy? Policy { get; private set; }
        public Task<List<GuardianPolicy>> GetAllAsync() => Task.FromResult(Policy is null ? [] : new List<GuardianPolicy> { Policy });
        public Task<GuardianPolicy> UpsertEvidenceAsync(GuardianPolicy policy) { Policy = policy; return Task.FromResult(policy); }
        public Task SetLifecycleAsync(Guid id, string lifecycle) { if (Policy?.Id == id) Policy.Lifecycle = lifecycle; return Task.CompletedTask; }
        public Task AddApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, bool counterfactual) => Task.CompletedTask;
        public Task<bool> HasApplicationAsync(Guid policyId, long? taskId) => Task.FromResult(false);
        public Task CompleteApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, string outcome, int strategicMinutes) => Task.CompletedTask;
    }
}
