using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianDecisionOutcomeRepository
{
    Task UpsertAsync(GuardianDecisionOutcome value);
    Task<IReadOnlyList<GuardianDecisionOutcome>> GetPendingAsync(int count = 200);
    Task<IReadOnlyList<GuardianDecisionOutcome>> GetRecentAsync(int count = 200);
}
