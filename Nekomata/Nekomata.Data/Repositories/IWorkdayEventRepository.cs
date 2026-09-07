using Nekomata.Models.Guardian;

namespace Nekomata.Data.Repositories;

public interface IWorkdayEventRepository
{
    Task AddAsync(WorkdayEvent entry);
    Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500);
    Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100);
    Task<bool> ExistsAsync(string eventType, string externalId);
}
