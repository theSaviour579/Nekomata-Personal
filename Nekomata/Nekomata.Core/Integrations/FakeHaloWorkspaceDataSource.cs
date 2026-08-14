using Nekomata.Models.Common;
using Nekomata.Models.Integrations;

namespace Nekomata.Core.Integrations;

public class FakeHaloWorkspaceDataSource
    : IWorkspaceDataSource
{
    public string Name => "Halo";

    public Task<WorkspaceDataSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = new WorkspaceDataSnapshot
        {
            SourceName = Name,
            RetrievedAt = DateTime.Now
        };

        snapshot.IntegrationMissions.Add(
         new IntegrationMission
         {
             SourceType = "Halo",

             SourceRecordId = "HALO-1001",

             Title = "Investigate failed backup",

             Description =
                 "One of the overnight backup jobs failed.",

             Customer = "Trycare",

             AssignedTo = "David",

             Status = "In Progress",

             Priority = TaskPriorities.High,

             BusinessValue = 15000,

             EstimatedMinutes = 45,

             CustomerImpact = true,

             RevenueImpact = false,

             SecurityRelated = false,

             CreatedAt =
                 DateTime.Now.AddHours(-5),

             DueAt =
                 DateTime.Now.AddHours(2),

             SlaExpiresAt =
                 DateTime.Now.AddHours(2),

             Tags =
             [
                 "Infrastructure",
                    "Backup",
                    "Server"
             ]
         });
        snapshot.Health =
    new IntegrationHealth
    {
        Connected = true,

        LastSuccessfulSync =
            DateTime.Now,

        Status =
            "Connected",

        Error =
            null,

        RecordsLoaded =
            snapshot.IntegrationMissions.Count
    };
        return Task.FromResult(snapshot);
    }
}