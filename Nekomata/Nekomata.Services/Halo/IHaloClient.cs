namespace Nekomata.Services.Halo;

public interface IHaloClient
{
    Task<IReadOnlyList<HaloTicket>> GetMyTicketsAsync(
        CancellationToken cancellationToken = default);
}