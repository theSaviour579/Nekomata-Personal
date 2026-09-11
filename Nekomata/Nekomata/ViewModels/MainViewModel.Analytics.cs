using CommunityToolkit.Mvvm.ComponentModel;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Services;
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

    [ObservableProperty]
    private PersonalValueSummary? valueLeverage;

    [ObservableProperty]
    private string valueLeverageStatus = "Open Value to map completed work to the goals for your role.";

    [ObservableProperty]
    private bool valueLeverageBusy;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task ShowValueLeverageAsync()
    {
        WorkspaceMode = Nekomata.Models.Workspace.WorkspaceMode.ValueLeverage;
        await RefreshValueLeverageAsync(false);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task RefreshValueLeverageAsync() => RefreshValueLeverageAsync(false);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task RegenerateRoleGoalsAsync() => RefreshValueLeverageAsync(true);

    private async Task RefreshValueLeverageAsync(bool regenerate)
    {
        if (ValueLeverageBusy) return;
        ValueLeverageBusy = true;
        ValueLeverageStatus = regenerate ? "Rebuilding goals for your job scope…" : "Reading your role and this week's completed work…";
        try
        {
            var role = await _services.GetRequiredService<PersonalRoleProfileService>().ResolveAsync(regenerate);
            ValueLeverage = await _services.GetRequiredService<PersonalValueService>().BuildAsync(role, DateTime.Now);
            ValueLeverageStatus = $"Goals tailored for {role.JobTitle} · role source: {role.Source} · updated {role.GeneratedAt.LocalDateTime:g}.";
        }
        catch (Exception ex) { ValueLeverageStatus = "Value could not be refreshed: " + ex.Message; }
        finally { ValueLeverageBusy = false; }
    }
}
