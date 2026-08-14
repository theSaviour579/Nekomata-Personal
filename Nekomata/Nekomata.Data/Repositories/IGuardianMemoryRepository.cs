using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianMemoryRepository
{
    Task<long> AddAsync(GuardianMemory memory);

    Task<List<GuardianMemory>> GetRecentAsync(int count = 25);

    Task<List<GuardianMemory>> GetProjectMemoriesAsync(
        long projectId,
        int count = 25);

    Task<List<GuardianMemory>> SearchAsync(
        string text,
        int count = 25);
}