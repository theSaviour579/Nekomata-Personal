namespace Nekomata.Core.Planning;

public interface IRoleCorrectionStore
{
    Task<IReadOnlyList<RoleCorrectionOutcome>> LoadAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(RoleCorrectionOutcome value, CancellationToken cancellationToken = default);
}

public interface IRoleBiasHistoryStore
{
    Task<IReadOnlyList<RoleBiasSnapshot>> LoadAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(IEnumerable<RoleBiasSnapshot> values, CancellationToken cancellationToken = default);
}
