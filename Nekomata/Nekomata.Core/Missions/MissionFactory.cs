using Nekomata.Models.Missions;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Missions;

public class MissionFactory : IMissionFactory
{
    public Mission Create(MissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var missionMinutes =
            candidate.SourceType.Equals(
                "Project",
                StringComparison.OrdinalIgnoreCase)
                ? Math.Clamp(candidate.EstimatedMinutes, 30, 120)
                : Math.Max(candidate.EstimatedMinutes, 1);

        return new Mission
        {
            TaskId = candidate.TaskId,
            ProjectId = candidate.ProjectId,
            SourceType = candidate.SourceType,
            SourceRecordId = candidate.SourceRecordId,

            Title = candidate.Title,
            Score = candidate.Score,
            
            GuardianReasons =
    candidate.GuardianReasons
        .ToList(),

            EstimatedDuration =
                TimeSpan.FromMinutes(missionMinutes),

            Status = "READY",
            Progress = candidate.Progress,

            BusinessValue = candidate.BusinessValue,

            RecommendationReason =
                candidate.RecommendationReason,

            ThreatLevel =
                CalculateThreatLevel(candidate),

            StartBefore =
                CalculateStartBefore(
                    candidate,
                    missionMinutes),

            ScoreFactors = candidate.ScoreFactors
    .Select(factor => new MissionScoreFactor
    {
        Category = factor.Category,
        Name = factor.Name,
        Explanation = factor.Explanation,
        Points = factor.Points
    })
    .ToList(),
            Strengths = candidate.Strengths.ToList(),

            Risks = candidate.Risks.ToList(),

            Decision =
new GuardianDecision
{
    Summary = candidate.GuardianDecision
},
        };
    }

    private static string CalculateThreatLevel(
        MissionCandidate candidate)
    {
        if (candidate.AtRisk)
            return "HIGH";

        return candidate.Score switch
        {
            >= 80 => "HIGH",
            >= 50 => "MEDIUM",
            _ => "LOW"
        };
    }

    private static DateTime CalculateStartBefore(
    MissionCandidate candidate,
    int missionMinutes)
    {
        if (candidate.DueAt is null)
            return DateTime.Now.AddMinutes(45);

        var suggestedStart =
            candidate.DueAt.Value - TimeSpan.FromMinutes(missionMinutes);

        return suggestedStart < DateTime.Now
            ? DateTime.Now
            : suggestedStart;
    }
}