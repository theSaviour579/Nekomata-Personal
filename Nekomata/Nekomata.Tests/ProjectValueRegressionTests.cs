using Nekomata.Core.Missions.Candidates;
using Nekomata.Core.Missions.Scoring;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class ProjectValueRegressionTests
{
    [Fact]
    public void Business_value_scoring_distinguishes_one_point_two_million_from_one_hundred_thousand()
    {
        var scorer = new MissionCandidateScorer();
        var lower = new MissionCandidate { BusinessValue = 100_000m };
        var higher = new MissionCandidate { BusinessValue = 1_200_000m };

        scorer.Score(lower);
        scorer.Score(higher);

        Assert.True(higher.Score > lower.Score);
        Assert.True(
            higher.ScoreFactors.Single(factor => factor.Category == "Business Value").Points >
            lower.ScoreFactors.Single(factor => factor.Category == "Business Value").Points);
    }

    [Fact]
    public void Project_candidate_represents_a_bounded_next_action_not_the_whole_project()
    {
        var workspace = new NekomataWorkspace
        {
            Projects =
            [
                new NekomataProject
                {
                    Id = 42,
                    Name = "Strategic transformation",
                    Status = "Active",
                    EstimatedBusinessValue = 1_200_000m,
                    EstimatedRemainingMinutes = 2_400,
                    NextAction = "Confirm the supplier decision"
                }
            ]
        };

        var candidate = new ProjectMissionCandidateProvider().GetCandidates(workspace).Single();

        Assert.Equal("Strategic transformation · Next action", candidate.Title);
        Assert.Equal("Confirm the supplier decision", candidate.Description);
        Assert.Equal(100_000m, candidate.BusinessValue);
        Assert.Equal(1_200_000m, candidate.StrategicBusinessValue);
        Assert.Equal(120, candidate.EstimatedMinutes);
    }
}