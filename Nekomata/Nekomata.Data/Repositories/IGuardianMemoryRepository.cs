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

    /// <summary>Memories of one category, most important and newest first (e.g. the facts the user asked Guardian to remember).</summary>
    Task<List<GuardianMemory>> GetByCategoryAsync(string category, int count = 50) => Task.FromResult(new List<GuardianMemory>());

    /// <summary>Permanently removes a memory ("forget that ..."). Returns false if it no longer exists.</summary>
    Task<bool> DeleteAsync(long id) => Task.FromResult(false);
}