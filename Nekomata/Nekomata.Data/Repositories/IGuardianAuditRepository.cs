using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianAuditRepository
{
    Task AddBatchAsync(IEnumerable<GuardianAuditEntry> entries);
    Task<List<GuardianAuditEntry>> GetRecentAsync(int count = 100);
    Task<List<GuardianAuditEntry>> GetBatchAsync(Guid batchId);
    Task MarkBatchUndoneAsync(Guid batchId, string message);
    Task MarkBatchConflictAsync(Guid batchId, string message);
}
