using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;

namespace Nekomata.Data.Local;

public sealed class LocalTaskRepository(LocalWorkspaceStore store) : ITaskRepository
{
    public Task<List<NekomataTask>> GetOpenTasksAsync() => store.ReadAsync(data => data.Tasks.Where(task => task.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).OrderByDescending(task => task.PriorityScore).ThenBy(task => task.DueAt).ThenBy(task => task.Id).ToList());
    public Task<NekomataTask?> GetByIdAsync(long id) => store.ReadAsync(data => data.Tasks.FirstOrDefault(task => task.Id == id));
    public Task<long> SaveAsync(NekomataTask task) => store.UpdateAsync(data => { if (task.Id == 0) { task.Id = data.Tasks.Select(item => item.Id).DefaultIfEmpty().Max() + 1; data.Tasks.Add(task); } else { var index = data.Tasks.FindIndex(item => item.Id == task.Id); if (index >= 0) data.Tasks[index] = task; else data.Tasks.Add(task); } return task.Id; });
    public async Task DeleteAsync(long id) => await store.UpdateAsync(data => data.Tasks.RemoveAll(task => task.Id == id));
    public async Task CompleteAsync(long taskId) => await store.UpdateAsync(data => SetStatus(data, taskId, "Completed", DateTime.Now));
    public async Task ReopenAsync(long taskId) => await store.UpdateAsync(data => SetStatus(data, taskId, "Open", null));
    public Task<int> CompleteOpenTasksForProjectAsync(long projectId) => store.UpdateAsync(data => { var tasks = data.Tasks.Where(task => task.ProjectId == projectId && task.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).ToList(); foreach (var task in tasks) { task.Status = "Completed"; task.CompletedAt = DateTime.Now; } return tasks.Count; });
    private static bool SetStatus(LocalWorkspaceData data, long id, string status, DateTime? completedAt) { var task = data.Tasks.FirstOrDefault(item => item.Id == id); if (task is null) return false; task.Status = status; task.CompletedAt = completedAt; return true; }
}

public sealed class LocalProjectRepository(LocalWorkspaceStore store) : IProjectRepository
{
    public Task<List<NekomataProject>> GetAllAsync() => store.ReadAsync(data => data.Projects.OrderByDescending(project => project.AtRisk).ThenByDescending(project => project.EstimatedBusinessValue).ThenBy(project => project.DueAt).ThenBy(project => project.Name).ToList());
    public Task<NekomataProject?> GetByIdAsync(long id) => store.ReadAsync(data => data.Projects.FirstOrDefault(project => project.Id == id));
    public Task<long> SaveAsync(NekomataProject project) => store.UpdateAsync(data => { if (project.Id == 0) { project.Id = data.Projects.Select(item => item.Id).DefaultIfEmpty().Max() + 1; project.CreatedAt = DateTime.Now; data.Projects.Add(project); } else { var index = data.Projects.FindIndex(item => item.Id == project.Id); if (index >= 0) data.Projects[index] = project; else data.Projects.Add(project); } project.UpdatedAt = DateTime.Now; return project.Id; });
    public async Task DeleteAsync(long id) => await store.UpdateAsync(data => data.Projects.RemoveAll(project => project.Id == id));
}

public sealed class LocalMissionSessionRepository(LocalWorkspaceStore store) : IMissionSessionRepository
{
    public async Task SaveAsync(MissionSession session) => await store.UpdateAsync(data => { session.Id = data.MissionSessions.Select(item => item.Id).DefaultIfEmpty().Max() + 1; data.MissionSessions.Add(session); return session.Id; });
    public Task<List<MissionSession>> GetRecentAsync(int count) => store.ReadAsync(data => data.MissionSessions.OrderByDescending(item => item.FinishedAt).Take(count).ToList());
    public Task<List<MissionSession>> GetTodayAsync() => store.ReadAsync(data => data.MissionSessions.Where(item => item.FinishedAt.Date == DateTime.Today).OrderByDescending(item => item.FinishedAt).ToList());
    public Task<List<MissionSession>> GetAllAsync() => store.ReadAsync(data => data.MissionSessions.OrderByDescending(item => item.FinishedAt).ToList());
    public Task<List<MissionSession>> GetBetweenAsync(DateTime from, DateTime to) => store.ReadAsync(data => data.MissionSessions.Where(item => item.FinishedAt >= from && item.FinishedAt < to).OrderByDescending(item => item.FinishedAt).ToList());
}

public sealed class LocalGuardianMemoryRepository(LocalWorkspaceStore store) : IGuardianMemoryRepository
{
    public Task<long> AddAsync(GuardianMemory memory) => store.UpdateAsync(data => { memory.Id = data.GuardianMemories.Select(item => item.Id).DefaultIfEmpty().Max() + 1; memory.CreatedAt = DateTime.Now; data.GuardianMemories.Add(memory); return memory.Id; });
    public Task<List<GuardianMemory>> GetRecentAsync(int count = 25) => store.ReadAsync(data => data.GuardianMemories.OrderByDescending(item => item.Importance).ThenByDescending(item => item.CreatedAt).Take(count).ToList());
    public Task<List<GuardianMemory>> GetProjectMemoriesAsync(long projectId, int count = 25) => store.ReadAsync(data => data.GuardianMemories.Where(item => item.ProjectId == projectId).OrderByDescending(item => item.Importance).ThenByDescending(item => item.CreatedAt).Take(count).ToList());
    public Task<List<GuardianMemory>> SearchAsync(string text, int count = 25) => string.IsNullOrWhiteSpace(text) ? Task.FromResult(new List<GuardianMemory>()) : store.ReadAsync(data => data.GuardianMemories.Where(item => Contains(item.Summary, text) || Contains(item.Detail, text) || Contains(item.Category, text) || Contains(item.Source, text)).OrderByDescending(item => item.Importance).ThenByDescending(item => item.CreatedAt).Take(count).ToList());
    private static bool Contains(string? value, string text) => value?.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) == true;
}

public sealed class LocalGuardianAuditRepository(LocalWorkspaceStore store) : IGuardianAuditRepository
{
    public async Task AddBatchAsync(IEnumerable<GuardianAuditEntry> entries) => await store.UpdateAsync(data => { foreach (var entry in entries) { entry.Id = data.GuardianAudit.Select(item => item.Id).DefaultIfEmpty().Max() + 1; data.GuardianAudit.Add(entry); } return true; });
    public Task<List<GuardianAuditEntry>> GetRecentAsync(int count = 100) => store.ReadAsync(data => data.GuardianAudit.OrderByDescending(item => item.AppliedAt).ThenByDescending(item => item.Id).Take(Math.Clamp(count, 1, 500)).ToList());
    public Task<List<GuardianAuditEntry>> GetBatchAsync(Guid batchId) => store.ReadAsync(data => data.GuardianAudit.Where(item => item.BatchId == batchId).OrderByDescending(item => item.Id).ToList());
    public Task MarkBatchUndoneAsync(Guid batchId, string message) => SetStatusAsync(batchId, "Undone", message, true);
    public Task MarkBatchConflictAsync(Guid batchId, string message) => SetStatusAsync(batchId, "Conflict", message, false);
    private async Task SetStatusAsync(Guid batchId, string status, string message, bool undone) => await store.UpdateAsync(data => { foreach (var item in data.GuardianAudit.Where(item => item.BatchId == batchId)) { item.Status = status; item.UndoMessage = message; if (undone) item.UndoneAt = DateTime.Now; } return true; });
}
