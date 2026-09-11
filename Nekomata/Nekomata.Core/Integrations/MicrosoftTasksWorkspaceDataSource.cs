using Nekomata.Integrations.MicrosoftGraph.Tasks;
using Nekomata.Models.Integrations;

namespace Nekomata.Core.Integrations;

public sealed class MicrosoftTasksWorkspaceDataSource(IMicrosoftTaskService tasks) : IWorkspaceDataSource
{
    public string Name => "Microsoft Tasks";

    public async Task<WorkspaceDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new WorkspaceDataSnapshot { SourceName = Name, RetrievedAt = DateTime.Now };
        var imported = await tasks.GetOpenTasksAsync(cancellationToken);
        if (!imported.AccountConnected)
        {
            snapshot.Health = new IntegrationHealth
            {
                Connected = false,
                Status = "Not connected",
                Error = "Connect a Microsoft account to import To Do and Planner tasks."
            };
            return snapshot;
        }
        var plannerKeys = imported.PlannerTasks.Select(DeduplicationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var combined = imported.PlannerTasks.Concat(imported.ToDoTasks.Where(item => !plannerKeys.Contains(DeduplicationKey(item))));

        foreach (var item in combined)
        {
            snapshot.IntegrationMissions.Add(new IntegrationMission
            {
                SourceType = item.Source,
                SourceRecordId = item.Id,
                Title = item.Title,
                Description = item.Description,
                ExternalUrl = item.ExternalUrl,
                Status = item.Status,
                Priority = item.Priority,
                DueAt = item.DueAt,
                CreatedAt = item.CreatedAt,
                LastUpdatedAt = item.LastUpdatedAt,
                EstimatedMinutes = 30,
                IsActionable = true,
                Tags = ["Microsoft", item.Source == "Microsoft Planner" ? "Planner" : "ToDo"]
            });
        }

        snapshot.Health = new IntegrationHealth
        {
            Connected = true,
            LastSuccessfulSync = DateTime.Now,
            Status = $"Connected · {imported.ToDoTasks.Count} personal To Do · {imported.PlannerTasks.Count} assigned Planner" +
                (imported.ExcludedSharedLists > 0 ? $" · {imported.ExcludedSharedLists} shared/unverifiable To Do lists excluded" : "") +
                (imported.ExcludedFlaggedEmailLists > 0 ? " · Flagged email tasks excluded" : ""),
            RecordsLoaded = snapshot.IntegrationMissions.Count
        };
        return snapshot;
    }

    internal static string DeduplicationKey(MicrosoftTaskItem item)
    {
        var title = string.Join(' ', item.Title.Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{title}|{item.DueAt:yyyy-MM-dd}";
    }
}
