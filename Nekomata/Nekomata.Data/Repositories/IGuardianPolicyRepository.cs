using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianPolicyRepository
{
    Task<List<GuardianPolicy>> GetAllAsync();
    Task<GuardianPolicy> UpsertEvidenceAsync(GuardianPolicy policy);
    Task SetLifecycleAsync(Guid id, string lifecycle);
    Task AddApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, bool counterfactual);
    Task<bool> HasApplicationAsync(Guid policyId, long? taskId);
    Task CompleteApplicationsAsync(IEnumerable<Guid> policyIds, long? taskId, long? projectId, string outcome, int strategicMinutes);
}
