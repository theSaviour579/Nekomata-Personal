using Nekomata.Core.Guardian;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianMissionDecisionBuilder
{
    private readonly MissionComparisonEngine
        _comparisonEngine;

    public GuardianMissionDecisionBuilder(
        MissionComparisonEngine comparisonEngine)
    {
        _comparisonEngine =
            comparisonEngine;
    }

    public GuardianDecision Build(
        Mission winner,
        IReadOnlyCollection<MissionCandidate> rankedCandidates,
        int confidence)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(rankedCandidates);

        var decision =
            new GuardianDecision
            {
                Headline =
                    $"Guardian selected {winner.Title}.",

                Recommendation =
                    winner.RecommendationReason,

                Summary =
                    BuildSummary(winner),

                Confidence =
                    confidence
            };

        decision.Reasons.Add(
            new GuardianReason
            {
                Category =
                    "Priority",

                Title =
                    "Highest Overall Priority",

                Explanation =
                    $"Guardian ranked this mission first with " +
                    $"an overall score of {winner.Score}.",

                Weight =
                    winner.Score,

                Positive =
                    true
            });

        if (winner.BusinessValue > 0)
        {
            decision.Reasons.Add(
                new GuardianReason
                {
                    Category =
                        "Business",

                    Title =
                        "High Business Impact",

                    Explanation =
                        $"Estimated commercial opportunity worth " +
                        $"approximately {winner.BusinessValue:C0}.",

                    Weight =
                        (int)Math.Min(
                            winner.BusinessValue / 1000m,
                            50),

                    Positive =
                        true
                });
        }

        decision.Reasons.Add(
            new GuardianReason
            {
                Category =
                    "Effort",

                Title =
                    "Achievable Today",

                Explanation =
                    $"Estimated effort is around " +
                    $"{FormatDuration(winner.EstimatedDuration)}, " +
                    $"making it suitable for a focused work session.",

                Weight =
                    5,

                Positive =
                    true
            });

        if (winner.StartBefore is not null)
        {
            decision.Reasons.Add(
                new GuardianReason
                {
                    Category =
                        "Timing",

                    Title =
                        "Recommended Next",

                    Explanation =
                        $"Guardian recommends starting before " +
                        $"{winner.StartBefore:HH:mm}.",

                    Weight =
                        8,

                    Positive =
                        true
                });
        }

        foreach (var candidate in rankedCandidates
                     .Where(candidate =>
                         !MatchesWinner(
                             candidate,
                             winner))
                     .Take(5))
        {
            var alternativeMission =
                CreateAlternativeMission(
                    candidate);

            var comparison =
                _comparisonEngine.Compare(
                    winner,
                    alternativeMission);

            decision.RejectedMissions.Add(
                new GuardianRejectedMission
                {
                    Title =
                        candidate.Title,

                    SourceType =
                        candidate.SourceType,

                    TaskId =
                        candidate.TaskId,

                    ProjectId =
                        candidate.ProjectId,

                    Score =
                        candidate.Score,

                    BusinessValue =
                        candidate.BusinessValue,

                    EstimatedMinutes =
                        candidate.EstimatedMinutes,

                    DueAt =
                        candidate.DueAt,

                    Progress =
                        candidate.Progress,

                    ThreatLevel =
                        CalculateThreatLevel(
                            candidate),

                    ScoreDifference =
                        winner.Score -
                        candidate.Score,

                    ComparisonReasons =
                        comparison.ToList(),

                    WhyNot =
                        BuildWhyNot(
                            winner,
                            candidate,
                            comparison),

                    Rank =
                        candidate.Rank,

                    RecommendationReason =
                        candidate.RecommendationReason
                });
        }

        return decision;
    }

    private static Mission CreateAlternativeMission(
        MissionCandidate candidate)
    {
        return new Mission
        {
            Title =
                candidate.Title,

            Score =
                candidate.Score,

            BusinessValue =
                candidate.BusinessValue,

            EstimatedDuration =
                TimeSpan.FromMinutes(
                    Math.Max(
                        candidate.EstimatedMinutes,
                        1)),

            ScoreFactors =
                candidate.ScoreFactors
                    .Select(factor =>
                        new MissionScoreFactor
                        {
                            Category =
                                factor.Category,

                            Name =
                                factor.Name,

                            Explanation =
                                factor.Explanation,

                            Points =
                                factor.Points
                        })
                    .ToList()
        };
    }

    private static string BuildSummary(
        Mission winner)
    {
        var parts =
            new List<string>
            {
                $"Selected because it achieved the highest " +
                $"overall score of {winner.Score}."
            };

        if (winner.BusinessValue > 0)
        {
            parts.Add(
                $"It carries an estimated business value of " +
                $"{winner.BusinessValue:C0}.");
        }

        if (winner.EstimatedDuration > TimeSpan.Zero)
        {
            parts.Add(
                $"Estimated effort is approximately " +
                $"{FormatDuration(winner.EstimatedDuration)}.");
        }

        return string.Join(
            " ",
            parts);
    }

    private static string BuildWhyNot(
     Mission winner,
     MissionCandidate candidate,
     IReadOnlyCollection<MissionComparisonReason> comparison)
    {
        var summary =
            new List<string>();

        if (candidate.BusinessValue >
            winner.BusinessValue)
        {
            summary.Add(
                $"offers an additional {(candidate.BusinessValue - winner.BusinessValue):C0} of estimated business value");
        }

        if (candidate.EstimatedMinutes >
            winner.EstimatedDuration.TotalMinutes)
        {
            summary.Add(
                $"requires approximately {(candidate.EstimatedMinutes - (int)winner.EstimatedDuration.TotalMinutes)} extra minutes");
        }

        var strongestWinner =
            comparison
                .Where(x => x.Difference > 0)
                .OrderByDescending(x => x.Difference)
                .FirstOrDefault();

        if (strongestWinner is not null)
        {
            summary.Add(
                $"is outweighed by the selected mission's stronger " +
                $"{GetFriendlyCategoryName(strongestWinner.Category)}");
        }

        if (summary.Count == 0)
        {
            return
                "Guardian found no significant advantage over the selected mission.";
        }

        return
            $"{candidate.Title} " +
            string.Join(", ", summary) +
            ".";
    }

    private static string CalculateThreatLevel(
        MissionCandidate candidate)
    {
        if (candidate.AtRisk)
            return "HIGH";

        return candidate.Score switch
        {
            >= 80 => "MEDIUM",
            _ => "LOW"
        };
    }

    private static bool MatchesWinner(
        MissionCandidate candidate,
        Mission winner)
    {
        return candidate.TaskId ==
               winner.TaskId
               &&
               candidate.ProjectId ==
               winner.ProjectId
               &&
               string.Equals(
                   candidate.SourceType,
                   winner.SourceType,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        var minutes =
            Math.Max(
                (int)Math.Round(
                    duration.TotalMinutes),
                0);

        var hours =
            minutes / 60;

        var remainingMinutes =
            minutes % 60;

        if (hours == 0)
            return $"{remainingMinutes} minutes";

        if (remainingMinutes == 0)
        {
            return hours == 1
                ? "1 hour"
                : $"{hours} hours";
        }

        return $"{hours}h {remainingMinutes}m";
    }
    private static string GetFriendlyCategoryName(
    string category)
    {
        return category switch
        {
            "Base" =>
                "overall strategic priority",

            "Business Value" =>
                "commercial impact",

            "Priority" =>
                "operational priority",

            "Urgency" =>
                "time sensitivity",

            "Risk" =>
                "risk profile",

            "Effort" =>
                "execution efficiency",

            "Progress" =>
                "completion progress",

            _ =>
                category.ToLowerInvariant()
        };
    }
}