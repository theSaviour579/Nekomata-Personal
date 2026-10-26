using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianInterruptionRepository
{
    Task<long> AddAsync(GuardianInterruption interruption);
    Task<List<GuardianInterruption>> GetTodayAsync();
    Task<List<GuardianInterruption>> GetAllAsync();
    Task MarkResumedAsync(long id);
}
