using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianWorkThreadRepository
{
    Task<GuardianWorkThread> GetOrCreateForProjectAsync(long projectId, string title, Guid sourceEventId);
    Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink link);
    Task AddEvidenceAsync(GuardianCorrelationEvidence evidence);
    Task ClearEvidenceAsync(Guid linkId) => Task.CompletedTask;
    Task<GuardianWorkThreadLink?> FindLinkAsync(string entityType, string entityKey, WorkThreadLinkStatus? status = null);
    Task<List<GuardianWorkThread>> GetActiveAsync();
    Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid linkId);
    Task UpdateEvaluationAsync(Guid linkId, int confidence, WorkThreadLinkStatus status, string linkMethod);
}
