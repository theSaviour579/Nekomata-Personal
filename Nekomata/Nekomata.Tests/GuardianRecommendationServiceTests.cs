using Nekomata.Core.Guardian.Recommendations;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianRecommendationServiceTests
{
    [Fact]
    public void Assigned_planner_candidate_is_available_as_dashboard_recommendation()
    {
        var workspace = new NekomataWorkspace
        {
            RankedMissionCandidates =
            [
                new MissionCandidate
                {
                    SourceType = "Microsoft Planner", SourceRecordId = "planner-21", Title = "Prepare customer review",
                    IsActionable = true, Rank = 1, Score = 53, EstimatedMinutes = 30, RecommendationReason = "Due today."
                }
            ]
        };

        var recommendation = new GuardianRecommendationService().GetTopRecommendation(workspace);

        Assert.NotNull(recommendation);
        Assert.Equal("Prepare customer review", recommendation.Title);
        Assert.Equal("Microsoft Planner", recommendation.RecommendationType);
    }
}
