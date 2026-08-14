using Nekomata.Core.Missions;
using Nekomata.Core.Missions.Candidates;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class MissionContinuationTests
{
    [Fact]
    public void Elapsed_session_is_added_to_existing_task_effort()
    {
        var task = new NekomataTask { EstimatedMinutes = 180, ActualMinutes = 60 };

        var update = MissionEffortTracker.ApplyElapsed(
            task, TimeSpan.FromMinutes(59.5));

        Assert.Equal(120, task.ActualMinutes);
        Assert.Equal(60, update.RemainingMinutes);
        Assert.Equal(2d / 3d, update.Progress, 6);
    }

    [Fact]
    public void Effort_is_capped_at_the_task_estimate()
    {
        var task = new NekomataTask { EstimatedMinutes = 90, ActualMinutes = 80 };

        var update = MissionEffortTracker.ApplyElapsed(
            task, TimeSpan.FromMinutes(30));

        Assert.Equal(90, task.ActualMinutes);
        Assert.Equal(0, update.RemainingMinutes);
        Assert.Equal(1, update.Progress);
    }

    [Fact]
    public void Future_task_mission_uses_remaining_effort_and_progress()
    {
        var workspace = new NekomataWorkspace
        {
            Tasks =
            [
                new NekomataTask
                {
                    Id = 4,
                    Title = "YoY Spend Deep Dive",
                    Status = "Open",
                    EstimatedMinutes = 180,
                    ActualMinutes = 60
                }
            ]
        };

        var candidate = Assert.Single(
            new TaskMissionCandidateProvider().GetCandidates(workspace));

        Assert.Equal(120, candidate.EstimatedMinutes);
        Assert.Equal(1d / 3d, candidate.Progress, 6);
    }

    [Fact]
    public void Calendar_session_is_capped_to_the_active_block()
    {
        var now = new DateTimeOffset(
            2026, 8, 14, 11, 0, 0, TimeSpan.Zero);

        var sessionEstimate = MissionSessionEstimateCalculator.Calculate(
            TimeSpan.FromHours(3), true, now, now.AddHours(1));

        Assert.Equal(TimeSpan.FromHours(1), sessionEstimate);
    }

    [Fact]
    public void Unscheduled_session_uses_all_remaining_task_effort()
    {
        var estimate = MissionSessionEstimateCalculator.Calculate(
            TimeSpan.FromHours(2), false, DateTimeOffset.UtcNow, null);

        Assert.Equal(TimeSpan.FromHours(2), estimate);
    }

    [Fact]
    public void Active_session_keeps_its_pinned_mission_after_refresh()
    {
        var active = new Nekomata.Models.Missions.Mission
        {
            TaskId = 4,
            Title = "YoY Spend Deep Dive"
        };
        var incoming = new Nekomata.Models.Missions.Mission
        {
            TaskId = 99,
            Title = "New standard mission"
        };

        var resolved = ActiveMissionFocusPolicy.Resolve(true, active, incoming);

        Assert.Same(active, resolved);
    }

    [Fact]
    public void Inactive_session_accepts_the_refreshed_mission()
    {
        var previous = new Nekomata.Models.Missions.Mission { Title = "Previous" };
        var incoming = new Nekomata.Models.Missions.Mission { Title = "New priority" };

        var resolved = ActiveMissionFocusPolicy.Resolve(false, previous, incoming);

        Assert.Same(incoming, resolved);
    }}