using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /*
     * Completed projects remain in Workspace.Projects so Guardian can
     * reason about historical work, but the dashboard only shows active
     * projects.
     */
    public IEnumerable<NekomataProject> VisibleProjects =>
        Workspace.Projects.Where(project =>
            !string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "Closed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "On Hold", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<MissionCandidate> NextMissionCandidates =>
        Workspace.RankedMissionCandidates
            .Skip(1)
            .Take(3);

    public IReadOnlyList<NekomataTask> TodayTasks =>
        Workspace.Tasks
            .Where(task =>
                task.DueAt is not null &&
                task.DueAt.Value.Date == CurrentDateTime.Date)
            .OrderBy(task => task.DueAt)
            .ThenByDescending(task => task.PriorityScore)
            .Take(6)
            .ToList();

    public int ActiveProjectCount =>
        VisibleProjects.Count();

    public int AtRiskProjectCount =>
        VisibleProjects.Count(project => project.AtRisk);

    public decimal ActiveProjectValue =>
        VisibleProjects.Sum(project =>
            project.EstimatedBusinessValue);

    public string BriefingSummary =>
        Workspace.Briefing.AiSummary;

    public string DashboardSummary
    {
        get
        {
            var todayCount = TodayTasks.Count;

            return todayCount switch
            {
                0 =>
                    "No tasks are due today. Guardian can focus on your next strategic priority.",

                1 =>
                    "You have one task due today.",

                _ =>
                    $"You have {todayCount} tasks due today."
            };
        }
    }

    partial void OnWorkspaceChanged(
        NekomataWorkspace value)
    {
        OnPropertyChanged(nameof(VisibleProjects));
        OnPropertyChanged(nameof(NextMissionCandidates));
        OnPropertyChanged(nameof(TodayTasks));

        OnPropertyChanged(nameof(ActiveProjectCount));
        OnPropertyChanged(nameof(AtRiskProjectCount));
        OnPropertyChanged(nameof(ActiveProjectValue));

        OnPropertyChanged(nameof(DashboardSummary));
        OnPropertyChanged(nameof(BriefingSummary));
        OnPropertyChanged(nameof(UrgentHaloTickets));
        OnPropertyChanged(nameof(HasUrgentHaloTickets));
        OnPropertyChanged(nameof(HaloIntegrationStatus));
        OnPropertyChanged(nameof(IsKnowBe4Connected));
        OnPropertyChanged(nameof(KnowBe4FailureCount));
        OnPropertyChanged(nameof(KnowBe4IntegrationStatus));
        OnPropertyChanged(nameof(IsSpotifyConfigured));
        OnPropertyChanged(nameof(SpotifyIntegrationStatus));
        OnPropertyChanged(nameof(HaloWatchlistTickets));
        OnPropertyChanged(nameof(HaloWatchlistCount));
        OnPropertyChanged(nameof(HaloWatchlistChaseCount));
        OnPropertyChanged(nameof(HaloWatchlistButtonLabel));
        OnPropertyChanged(nameof(HaloWatchlistSummary));
        OnPropertyChanged(nameof(BattleTickets));
        OnPropertyChanged(nameof(BattlePrimaryTicket));
        OnPropertyChanged(nameof(ActiveBattleCount));
        OnPropertyChanged(nameof(BattleIncidentLabel));
        OnPropertyChanged(nameof(BattleSlaCountdown));
        OnPropertyChanged(nameof(BattleSlaDeadline));
        OnPropertyChanged(nameof(BattleEffortLabel));

        OnPropertyChanged(nameof(HasBriefingObjective));
        OnPropertyChanged(nameof(HasYesterdayActivity));
        OnPropertyChanged(nameof(HasBriefingRisks));
        OnPropertyChanged(nameof(HasBriefingRecommendations));
    }

    partial void OnCurrentDateTimeChanged(
        DateTime value)
    {
        UpdateGreeting();
        OnPropertyChanged(nameof(BattleSlaCountdown));

        /*
         * Refresh date-sensitive bindings when the minute changes.
         * This also ensures the list changes correctly after midnight.
         */
        if (value.Second == 0)
        {
            OnPropertyChanged(nameof(TodayTasks));
            OnPropertyChanged(nameof(DashboardSummary));
            RefreshTimeAwareCapacity();
            _ = RefreshCalendarAwareObjectiveAsync();
        }
    }

    private void UpdateGreeting()
    {
        var name = string.IsNullOrWhiteSpace(_personalProfile.Current.DisplayName)
            ? "there"
            : _personalProfile.Current.DisplayName;

        Greeting = CurrentDateTime.Hour switch
        {
            < 12 => $"Good morning, {name}.",
            < 17 => $"Good afternoon, {name}.",
            _ => $"Good evening, {name}."
        };
        Workspace.Briefing.Greeting = Greeting;
        OnPropertyChanged(nameof(Workspace));
    }
    public bool HasBriefingObjective =>
    !string.IsNullOrWhiteSpace(
        Workspace.Briefing.ObjectiveTitle);

    public bool HasYesterdayActivity =>
        Workspace.Briefing.MissionsCompletedYesterday > 0 ||
        Workspace.Briefing.MissionsCancelledYesterday > 0;

    public bool HasBriefingRisks =>
        Workspace.Briefing.GuardianRisks.Count > 0;

    public bool HasBriefingRecommendations =>
        Workspace.Briefing.GuardianReasons.Count > 0;

}
