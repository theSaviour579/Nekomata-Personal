using Nekomata.Models.Projects;

namespace Nekomata.Data.Repositories;

public interface IProjectRepository
{
    Task<List<NekomataProject>> GetAllAsync();

    Task<NekomataProject?> GetByIdAsync(long id);

    Task<long> SaveAsync(NekomataProject project);

    Task DeleteAsync(long id);
}