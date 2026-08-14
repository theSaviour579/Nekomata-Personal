using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Analytics;

public interface IMissionAnalyticsService
{
    Task<MissionAnalytics> GetTodayAsync();
}