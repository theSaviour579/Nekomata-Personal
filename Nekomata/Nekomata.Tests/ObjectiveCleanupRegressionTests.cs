using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Builders;
using Nekomata.Core.Guardian.Learning;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Missions;
using Nekomata.Core.Missions.Candidates;
using Nekomata.Core.Missions.Scoring;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class ObjectiveCleanupRegressionTests
{
    [Fact]
    public void External_candidates_with_null_internal_ids_remain_distinct()
    {
        var winner = new Mission { Title = "Task A", SourceType = "Planner", SourceRecordId = "100", Score = 90 };
        var candidates = new[]
        {
            new MissionCandidate { Title = "Task A", SourceType = "Planner", SourceRecordId = "100", Score = 90, Rank = 1 },
            new MissionCandidate { Title = "Task B", SourceType = "Planner", SourceRecordId = "200", Score = 80, Rank = 2 }
        };

        var decision = new GuardianMissionDecisionBuilder(new MissionComparisonEngine())
            .Build(winner, candidates, 90);

        var alternative = Assert.Single(decision.RejectedMissions);
        Assert.Equal("Task B", alternative.Title);
        Assert.Equal("200", alternative.SourceRecordId);
    }

    [Fact]
    public void Override_preserves_external_record_identity()
    {
        var workspace = new NekomataWorkspace { CurrentMission = new Mission { Title = "Original" } };
        var service = new MissionOverrideService(
            new GuardianMissionDecisionBuilder(new MissionComparisonEngine()), new FakeLearning());

        service.Override(workspace, new GuardianRejectedMission
        {
            Title = "Task B", SourceType = "Planner", SourceRecordId = "200", EstimatedMinutes = 30
        });

        Assert.Equal("200", workspace.CurrentMission.SourceRecordId);
    }

    [Fact]
    public void Override_marks_manual_selection_as_authoritative()
    {
        var workspace = new NekomataWorkspace { CurrentMission = new Mission { Title = "Guardian choice" } };
        var service = new MissionOverrideService(
            new GuardianMissionDecisionBuilder(new MissionComparisonEngine()), new FakeLearning());

        var result = service.Override(workspace, new GuardianRejectedMission
        {
            Title = "User choice", SourceType = "Task", TaskId = 42, EstimatedMinutes = 30
        });

        Assert.True(result.Success);
        Assert.Equal("User choice", workspace.CurrentMission.Title);
        Assert.Equal("MANUALLY SELECTED", workspace.CurrentMission.Status);
        Assert.Contains("Selected manually", workspace.CurrentMission.RecommendationReason);
    }

    [Fact]
    public void Task_composite_priority_is_not_counted_as_a_second_base_factor()
    {
        var workspace = new NekomataWorkspace
        {
            Tasks = [new NekomataTask
            {
                Id = 1, Title = "Purchasing", Status = "Open", Priority = "High",
                PriorityScore = 99, EstimatedBusinessValue = 57_709m, EstimatedMinutes = 75
            }]
        };
        var candidate = Assert.Single(new TaskMissionCandidateProvider().GetCandidates(workspace));

        new MissionCandidateScorer().Score(candidate);

        Assert.DoesNotContain(candidate.ScoreFactors, item => item.Category == "Base");
        Assert.Equal(candidate.ScoreFactors.Sum(item => item.Points), candidate.Score);
        Assert.True(candidate.Score < 100);
    }

    [Fact]
    public void Retrospective_input_rejects_dates_outside_today()
    {
        var now = new DateTime(2026, 9, 2, 17, 0, 0);
        var success = RetrospectiveObjectiveInput.TryParseToday(
            new DateTime(2026, 9, 1, 14, 0, 0).ToString("g"),
            new DateTime(2026, 9, 1, 15, 0, 0).ToString("g"),
            now.Date, now, out _, out var error);

        Assert.False(success);
        Assert.Contains("today", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Paused_work_keeps_separate_calendar_segments()
    {
        var day = new DateTime(2026, 9, 2);
        var segments = MissionWorkSegmentBuilder.Build(
            [new MissionWorkSegment(day.AddHours(9), day.AddHours(9.5))],
            day.AddHours(10.5), day.AddHours(11), day.AddHours(9), TimeSpan.FromHours(1));

        Assert.Equal(2, segments.Count);
        Assert.Equal(day.AddHours(9.5), segments[0].End);
        Assert.Equal(day.AddHours(10.5), segments[1].Start);
    }

    private sealed class FakeLearning : IGuardianLearningService
    {
        public GuardianOverrideRecord RecordOverride(Mission originalMission, Mission selectedMission, NekomataWorkspace workspace) => new();
    }
}
