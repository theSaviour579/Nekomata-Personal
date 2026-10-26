namespace Nekomata.Data.Repositories;

public interface IGuardianSignalCheckpointRepository
{
    Task<DateTimeOffset?> GetAsync(string source);
    Task SetAsync(string source, DateTimeOffset observedThrough);
}
