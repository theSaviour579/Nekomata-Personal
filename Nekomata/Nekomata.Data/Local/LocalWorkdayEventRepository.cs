using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
namespace Nekomata.Data.Local;
public sealed class LocalWorkdayEventRepository(LocalWorkspaceStore store) : IWorkdayEventRepository
{
    public async Task AddAsync(WorkdayEvent entry) => await store.UpdateAsync(data =>
    {
        if (!string.IsNullOrWhiteSpace(entry.ExternalId) && data.WorkdayEvents.Any(x => x.EventType == entry.EventType && x.ExternalId == entry.ExternalId)) return false;
        entry.Id = data.WorkdayEvents.Select(x => x.Id).DefaultIfEmpty().Max()+1; data.WorkdayEvents.Add(entry); return true;
    });
    public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count=500) => store.ReadAsync(data => data.WorkdayEvents.Where(x => x.OccurredAt >= from && x.OccurredAt < to).OrderByDescending(x => x.OccurredAt).Take(count).ToList());
    public Task<List<WorkdayEvent>> SearchAsync(string query, int count=100) => store.ReadAsync(data => data.WorkdayEvents.Where(x => (x.Title+" "+x.Narrative).Contains(query,StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.OccurredAt).Take(count).ToList());
    public Task<bool> ExistsAsync(string eventType, string externalId) => store.ReadAsync(data => data.WorkdayEvents.Any(x => x.EventType == eventType && x.ExternalId == externalId));
}
