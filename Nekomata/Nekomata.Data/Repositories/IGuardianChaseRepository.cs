using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianChaseRepository
{
    Task<GuardianChaseAttempt?> GetPreparedAsync(Guid openLoopId);
    Task<int> GetAttemptCountAsync(Guid openLoopId);
    Task AddAsync(GuardianChaseAttempt attempt);
    Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status);
    Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid openLoopId);
    Task<bool> ApproveAsync(Guid id, DateTimeOffset approvedAt);
    Task RecordSendFailureAsync(Guid id, DateTimeOffset attemptedAt, string error);
    Task<bool> MarkSentAsync(Guid id, DateTimeOffset sentAt, DateTimeOffset nextChaseDueAt);
    Task<bool> CancelAsync(Guid id, DateTimeOffset cancelledAt) => Task.FromResult(false);
}
