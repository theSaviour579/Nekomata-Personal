using Nekomata.Models.Common;
using Nekomata.Models.Integrations;
using Nekomata.Services.KnowBe4;

namespace Nekomata.Core.Integrations;

public sealed class KnowBe4WorkspaceDataSource : IWorkspaceDataSource
{
    private readonly KnowBe4Client _client;
    private readonly KnowBe4AcknowledgementStore _acknowledgements;
    public string Name => "KnowBe4";

    public KnowBe4WorkspaceDataSource(
        KnowBe4Client client,
        KnowBe4AcknowledgementStore acknowledgements)
    {
        _client = client;
        _acknowledgements = acknowledgements;
    }

    public async Task<WorkspaceDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var failures = await _client.GetRecentFailuresAsync(cancellationToken);
        var snapshot = new WorkspaceDataSnapshot { SourceName = Name, RetrievedAt = DateTime.Now };
        foreach (var failure in failures.Where(failure => !_acknowledgements.IsAcknowledged(failure.EventId)))
        {
            snapshot.IntegrationMissions.Add(new IntegrationMission
            {
                SourceType = Name,
                SourceRecordId = failure.EventId,
                Title = $"Simulation failure · {failure.UserName}",
                Description = $"{failure.FailureTypes} in '{failure.TestName}' at {failure.OccurredAt.ToLocalTime():dd MMM HH:mm}.",
                AssignedTo = failure.Email,
                Status = "Review required",
                Priority = TaskPriorities.High,
                EstimatedMinutes = 15,
                SecurityRelated = true,
                IsActionable = true,
                RequiresImmediateAttention = true,
                CreatedAt = failure.OccurredAt.LocalDateTime,
                LastUpdatedAt = failure.OccurredAt.LocalDateTime,
                Tags = ["KnowBe4", "SimulationFailure"]
            });
        }
        snapshot.Health = new IntegrationHealth
        {
            Connected = true,
            LastSuccessfulSync = DateTime.Now,
            Status = snapshot.IntegrationMissions.Count == 0 ? "Connected" : $"Connected · {snapshot.IntegrationMissions.Count} recent failure{(snapshot.IntegrationMissions.Count == 1 ? string.Empty : "s")}",
            RecordsLoaded = snapshot.IntegrationMissions.Count
        };
        return snapshot;
    }
}