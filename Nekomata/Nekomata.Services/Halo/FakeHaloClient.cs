namespace Nekomata.Services.Halo;

public class FakeHaloClient
    : IHaloClient
{
    public Task<IReadOnlyList<HaloTicket>>
        GetMyTicketsAsync(
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HaloTicket> tickets =
        [
            new()
            {
                Id = 1001,

                Summary =
                    "Investigate failed backup",

                Customer =
                    "Trycare",

                Priority =
                    "High",

                Status =
                    "In Progress",

                Created =
                    DateTime.Now.AddHours(-5),

                Due =
                    DateTime.Now.AddHours(2),

                BusinessValue =
                    15000,

                CustomerImpact =
                    true,

                SecurityRelated =
                    false,

                EstimatedMinutes =
                    45
            }
        ];

        return Task.FromResult(tickets);
    }
}