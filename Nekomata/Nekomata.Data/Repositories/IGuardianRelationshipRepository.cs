using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IGuardianRelationshipRepository
{
    Task UpsertObservationAsync(GuardianRelationshipObservation observation);
    Task<GuardianRelationshipProfile?> GetProfileAsync(string contactAddress);
    Task<IReadOnlyList<GuardianRelationshipProfile>> GetProfilesAsync(int count = 100);
    Task SetExcludedAsync(string contactAddress, string displayName, bool excluded, string note = "");
}
