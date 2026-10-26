using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface ILearnedWorkflowRepository
{
    Task UpsertObservationAsync(WorkflowObservation observation);
    Task<List<WorkflowObservation>> GetObservationsAsync();
    Task<LearnedWorkflowPattern> UpsertPatternAsync(LearnedWorkflowPattern pattern);
    Task AddPromotionDecisionAsync(PolicyPromotionDecision decision);
    Task<LearnedWorkflowPattern?> FindByPolicyIdAsync(Guid policyId);
    Task MarkPromotedAsync(Guid patternId, Guid policyId);
}
