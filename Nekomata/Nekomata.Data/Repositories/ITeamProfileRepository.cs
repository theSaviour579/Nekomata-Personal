using Nekomata.Models.People;

namespace Nekomata.Data.Repositories;

public interface ITeamProfileRepository
{
    Task<IReadOnlyList<TeamMemberProfile>> GetAllAsync();
    Task<long> SaveAsync(TeamMemberProfile profile);
    Task DeleteAsync(long id);
}
