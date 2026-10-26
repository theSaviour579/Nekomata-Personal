using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public class WorkdayJournalServiceTests
{
    [Fact]
    public void Insights_CorrelateRecommendationDecisionAndOutcomeOnce()
    {
        var accepted = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var events = new[]
        {
            new WorkdayEvent { CorrelationId = accepted, RecommendationId = accepted, Recommendation = "Start A", UserDecision = "Accepted", Outcome = "Pending" },
            new WorkdayEvent { CorrelationId = accepted, RecommendationId = accepted, Recommendation = "Start A", UserDecision = "Accepted", Outcome = "Successful" },
            new WorkdayEvent { CorrelationId = rejected, RecommendationId = rejected, Recommendation = "Defer B", UserDecision = "Rejected", Outcome = "Started C" }
        };

        var insight = WorkdayJournalService.BuildInsights(events).First();

        Assert.Contains("1/2 recommendations accepted", insight.Detail);
        Assert.Contains("100% successful", insight.Detail);
    }

    [Fact]
    public void Insights_SumDeliveredValueFromCompletedObjectives()
    {
        var events = new[]
        {
            new WorkdayEvent { EventType = "ObjectiveCompleted", BusinessValue = 40000m },
            new WorkdayEvent { EventType = "ObjectiveCompleted", BusinessValue = 5000m }
        };
        Assert.Contains("45,000", WorkdayJournalService.BuildInsights(events).Single(x => x.Title == "Business value delivered").Detail);
    }
}
