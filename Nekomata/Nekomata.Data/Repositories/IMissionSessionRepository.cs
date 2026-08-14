using Nekomata.Models.Missions;

namespace Nekomata.Data.Repositories;

public interface IMissionSessionRepository
{
    Task SaveAsync(MissionSession session);

    Task<List<MissionSession>> GetRecentAsync(int count);

    Task<List<MissionSession>> GetTodayAsync();

    Task<List<MissionSession>> GetAllAsync();

    Task<List<MissionSession>> GetBetweenAsync(
        DateTime from,
        DateTime to);
}