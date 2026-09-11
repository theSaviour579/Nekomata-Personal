namespace Nekomata.Integrations.MicrosoftGraph.Tasks;

public interface IMicrosoftTaskService
{
    Task<MicrosoftTaskSnapshot> GetOpenTasksAsync(CancellationToken cancellationToken = default);
}

public sealed record MicrosoftTaskSnapshot(
    bool AccountConnected,
    IReadOnlyList<MicrosoftTaskItem> ToDoTasks,
    IReadOnlyList<MicrosoftTaskItem> PlannerTasks)
{
    public int ExcludedSharedLists { get; init; }
    public int ExcludedFlaggedEmailLists { get; init; }
}

public sealed record MicrosoftTaskItem(
    string Source,
    string Id,
    string Title,
    string Description,
    string Status,
    string Priority,
    DateTime? DueAt,
    DateTime? CreatedAt,
    DateTime? LastUpdatedAt,
    string ExternalUrl,
    string? PlanId = null);
