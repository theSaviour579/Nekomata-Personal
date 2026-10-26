using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianRecoveryPlannerTests
{
    [Fact]
    public void Build_PreservesObjectiveAndFixedMeetingWhileSelectingLowerRankedWork()
    {
        var now = new DateTime(2026, 8, 14, 13, 0, 0);
        var workspace = new NekomataWorkspace
        {
            Capacity = new CapacitySummary { ExpectedFinishAt = now.AddHours(4), PlannedMinutesToday = 300, AvailableMinutesToday = 250 },
            Timeline =
            [
                new MissionTimelineItem { Title = "Sell Pack Target Creation", StartAt = now.AddMinutes(-30), EndAt = now.AddMinutes(45) },
                new MissionTimelineItem { Title = "Leadership meeting", StartAt = now.AddHours(1), EndAt = now.AddHours(2), IsFixed = true }
            ],
            RankedMissionCandidates =
            [
                new MissionCandidate { TaskId = 10, Title = "Sell Pack Target Creation", Score = 90, DueAt = now, EstimatedMinutes = 90 },
                new MissionCandidate { TaskId = 20, Title = "Low-value admin", Score = 20, DueAt = now, EstimatedMinutes = 40 }
            ]
        };
        var interruption = new GuardianInterruption
        {
            TaskId = 10, MissionTitle = "Sell Pack Target Creation", LostMinutes = 35, RemainingMinutes = 80
        };

        var plan = new GuardianRecoveryPlanner().Build(workspace, interruption, now);

        Assert.NotNull(plan);
        Assert.Equal(20, plan.DeferredTaskId);
        Assert.Equal("Sell Pack Target Creation", plan.ProtectedObjective);
        Assert.Contains(plan.PreservedCommitments, x => x.Contains("Leadership meeting"));
        Assert.Equal(35, plan.ProtectedWindowOverrunMinutes);
    }

    [Fact]
    public void Build_IgnoresTrivialInterruptions() =>
        Assert.Null(new GuardianRecoveryPlanner().Build(new NekomataWorkspace(),
            new GuardianInterruption { LostMinutes = 4, RemainingMinutes = 60 }, DateTime.Now));

    [Fact]
    public void Forecast_recovery_is_previewable_and_moves_only_lower_ranked_work()
    {
        var now = new DateTime(2026, 9, 11, 14, 0, 0); // Friday
        var workspace = new NekomataWorkspace
        {
            CurrentMission = new Mission { Title = "Purchasing forecast" },
            RankedMissionCandidates =
            [
                new() { TaskId = 1, Title = "Critical incident", Score = 100, Priority = "Critical", DueAt = now, EstimatedMinutes = 90 },
                new() { TaskId = 2, Title = "Routine admin", Score = 20, Priority = "Normal", DueAt = now, EstimatedMinutes = 45 }
            ]
        };
        var forecast = new GuardianWorkloadForecast
        {
            MinutesAtRisk = 40, ConfidencePercent = 84, ProjectedFinishAt = new DateTimeOffset(now.AddHours(3)),
            InterruptionAllowanceMinutes = 25, ThreatenedCommitments = ["Routine admin"]
        };

        var result = new GuardianRecoveryPlanner().BuildFromForecast(workspace, forecast, now);

        Assert.NotNull(result);
        Assert.True(result.ForecastDriven);
        Assert.Equal(2, result.DeferredTaskId);
        Assert.Equal(DayOfWeek.Monday, result.DeferredUntil.DayOfWeek);
        Assert.Equal(84, result.ForecastConfidence);
    }
}
