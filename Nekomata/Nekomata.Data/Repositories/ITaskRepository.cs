using Nekomata.Models.Tasks;

namespace Nekomata.Data.Repositories;

public interface ITaskRepository
{
    Task<List<NekomataTask>> GetOpenTasksAsync();

    Task<NekomataTask?> GetByIdAsync(long id);

    Task<long> SaveAsync(NekomataTask task);

    Task DeleteAsync(long id);

    Task CompleteAsync(long taskId);

    Task ReopenAsync(long taskId);

    Task<int> CompleteOpenTasksForProjectAsync(long projectId);
}