using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianOpenLoopRepository
{
    Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop openLoop);
    Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200);
    Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200);
    Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence);
}
