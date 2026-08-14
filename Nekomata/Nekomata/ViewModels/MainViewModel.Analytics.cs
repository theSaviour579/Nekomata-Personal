using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using System.Collections.ObjectModel;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private MissionAnalytics analytics = new();

    public ObservableCollection<MissionSession>
        RecentMissionHistory
    { get; } = [];

    private async Task RefreshAnalyticsAsync()
    {
        Analytics =
            await _analyticsService.GetTodayAsync();

        Workspace.Briefing.MissionsCompletedToday = Analytics.MissionsCompletedToday;
        Workspace.Briefing.FocusMinutesToday =
            Math.Max(0, (int)Math.Round(Analytics.FocusTimeToday.TotalMinutes));
        OnPropertyChanged(nameof(Workspace));

        var recentSessions =
            await _missionSessionRepository.GetRecentAsync(10);

        RecentMissionHistory.Clear();

        foreach (var session in recentSessions)
        {
            RecentMissionHistory.Add(session);
        }
    }
}