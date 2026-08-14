using Nekomata.Models.Guardian;
namespace Nekomata.Models.Missions;

public class Mission
{
    public string Title { get; set; } = "";

    public string Description { get; set; } = "";

    public int Score { get; set; }

    public decimal BusinessValue { get; set; }

    public TimeSpan EstimatedDuration { get; set; }

    public DateTime? StartBefore { get; set; }

    public string Status { get; set; } = "Ready";

    public double Progress { get; set; }

    public string ThreatLevel { get; set; } = "Medium";

    public long? TaskId { get; set; }

    public long? ProjectId { get; set; }

    public string SourceType { get; set; } = "Task";

    public string? SourceRecordId { get; set; }

    public string RecommendationReason { get; set; } = "";

    public List<MissionScoreFactor> ScoreFactors { get; set; } = [];

    public List<string> Strengths { get; set; } = [];

    public List<string> Risks { get; set; } = [];
    public GuardianDecision Decision { get; set; } = new();

    public List<GuardianReason> GuardianReasons { get; set; } = [];

    public IReadOnlyList<MissionScoreGroup>
    GroupedScoreFactors =>
        ScoreFactors
            .GroupBy(f => f.Category)
            .Select(group =>
                new MissionScoreGroup
                {
                    Category = group.Key,
                    TotalPoints = group.Sum(f => f.Points),
                    Factors = group.ToList()
                })
            .ToList();
}