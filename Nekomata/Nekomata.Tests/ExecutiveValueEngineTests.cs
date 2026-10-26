using Nekomata.Core.Analytics;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Xunit;

namespace Nekomata.Tests;

public sealed class ExecutiveValueEngineTests
{
    [Fact]
    public void BuildsEpisodesWithoutDoubleCountingProjectValue()
    {
        var start = new DateTime(2026, 9, 7, 9, 0, 0);
        var sessions = new[]
        {
            Session(1, "Build purchasing forecast dataset", start, 120, 84_000, 9),
            Session(2, "Validate purchasing forecast report", start.AddDays(1), 60, 84_000, 9),
            Session(3, "Review phishing security failures", start.AddDays(2), 30, 0),
            Session(4, "Routine administration", start.AddDays(3), 30, 0)
        };
        var projects = new Dictionary<long, NekomataProject>
        {
            [9] = new() { Id = 9, Name = "Purchasing Forecast", Priority = "High", EstimatedBusinessValue = 84_000 }
        };

        var result = ExecutiveValueEngine.Analyse(sessions, projects, start.Date, start.Date.AddDays(5),
            session => session.Id == 1 ? 30 : 0, 20.31m);

        Assert.Equal(240, result.TotalMinutes);
        Assert.Equal(30, result.AdditionalMinutes);
        Assert.Equal(81.24m, result.LabourCost);
        Assert.Equal(84_000m, result.AssociatedFinancialValue);
        Assert.Equal(2, result.EstimatedCount);
        Assert.Equal(1, result.NonFinancialCount);
        Assert.Equal(1, result.ProxyCount);
        Assert.Contains(result.Areas, area => area.Name == "Reporting & data" && area.Minutes == 180);
        Assert.All(result.Episodes.Where(x => x.ProjectId == 9), episode => Assert.Equal("High", episode.StrategicRelevance));
        Assert.Equal(60, result.Episodes.First(x => x.ProjectId == 9).ValueConfidence);
        Assert.Contains(result.LeverageBands, band => band.Name == "High-leverage work");
    }

    [Fact]
    public void ChallengesRepeatedReactiveOvertimeAndNamesTheAlternative()
    {
        var start = new DateTime(2026, 9, 7, 17, 0, 0);
        var sessions = Enumerable.Range(1, 3)
            .Select(index => Session(index, $"Supplier support query {index}", start.AddDays(index - 1), 60, 0)).ToArray();
        var open = new[]
        {
            new NekomataTask { Title = "Automate the monthly roll-up", PriorityScore = 90, ExecutiveVisibility = 4,
                AutomationPotential = 5, EstimatedBusinessValue = 25_000 }
        };

        var result = ExecutiveValueEngine.Analyse(sessions, new Dictionary<long, NekomataProject>(),
            start.Date, start.Date.AddDays(5), _ => 30, 20.31m, open);

        Assert.Contains(result.Judgements, x => x.StartsWith("Opportunity cost:"));
        Assert.Contains(result.Judgements, x => x.StartsWith("Delegation opportunity:"));
        Assert.Contains(result.Judgements, x => x.StartsWith("Structural overload signal:"));
        Assert.Contains(result.Clusters, x => x.Name == "Operational support" && x.EpisodeCount == 3);
        Assert.Equal("Reactive work", result.Episodes[0].OperatingMode);
        Assert.True(result.Episodes[0].LeverageScore < 40);
    }

    private static MissionSession Session(long id, string title, DateTime start, int minutes, decimal value, long? projectId = null) => new()
    {
        Id = id, Title = title, SourceType = "Task", StartedAt = start, FinishedAt = start.AddMinutes(minutes),
        ActualDurationMinutes = minutes, EstimatedDurationMinutes = minutes, Completed = true,
        BusinessValue = value, ProjectId = projectId
    };
}
