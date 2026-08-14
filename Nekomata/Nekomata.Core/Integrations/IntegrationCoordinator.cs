using Nekomata.Models.Integrations;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Integrations;

public class IntegrationCoordinator
{
    private readonly IEnumerable<IWorkspaceDataSource>
        _sources;

    public IntegrationCoordinator(
        IEnumerable<IWorkspaceDataSource> sources)
    {
        _sources = sources;
    }

    public async Task<WorkspaceDataSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            new WorkspaceDataSnapshot
            {
                SourceName = "Combined",
                RetrievedAt = DateTime.Now
            };

        foreach (var source in _sources)
        {
            var startedAt =
                DateTime.UtcNow;

            try
            {
                var result =
                    await source.LoadAsync(
                        cancellationToken);

                snapshot.IntegrationMissions
                    .AddRange(
                        result.IntegrationMissions);

                snapshot.Notifications
                    .AddRange(
                        result.Notifications);

                snapshot.Integrations.Add(
                    new IntegrationStatus
                    {
                        Name =
                            source.Name,

                        Connected =
                            result.Health.Connected,

                        LastRefresh =
                            result.Health.LastSuccessfulSync,

                        MissionCount =
                            result.IntegrationMissions.Count,

                        Status =
                            result.Health.Status,

                        ErrorMessage =
                            result.Health.Error,

                        RefreshDuration =
                            DateTime.UtcNow -
                            startedAt
                    });
            }
            catch (Exception ex)
            {
                snapshot.Integrations.Add(
                    new IntegrationStatus
                    {
                        Name =
                            source.Name,

                        Connected =
                            false,

                        LastRefresh =
                            null,

                        MissionCount =
                            0,

                        Status =
                            "Failed",

                        ErrorMessage =
                            ex.Message,

                        RefreshDuration =
                            DateTime.UtcNow -
                            startedAt
                    });

                snapshot.Notifications.Add(
                    $"{source.Name} integration failed: " +
                    ex.Message);
            }
        }

        return snapshot;
    }
}