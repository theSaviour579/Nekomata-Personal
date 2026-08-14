using Nekomata.Models.Missions;

namespace Nekomata.Core.Missions.Scoring;

public class MissionCandidateScorer : IMissionCandidateScorer
{
    public void Score(MissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        candidate.ScoreFactors.Clear();

        AddBaseScore(candidate);
        AddPriorityScore(candidate);
        AddImmediateAttentionScore(candidate);
        AddUrgencyScore(candidate);
        AddBusinessValueScore(candidate);
        AddStrategicExposureScore(candidate);
        AddRiskScore(candidate);
        AddProgressScore(candidate);
        AddEffortScore(candidate);

        candidate.Score =
            candidate.ScoreFactors.Sum(factor => factor.Points);
    }

    private static void AddBaseScore(
        MissionCandidate candidate)
    {
        if (candidate.BaseScore <= 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Base",

            Name = "Source Priority",

            Explanation =
                "Priority inherited from the source work item.",

            Points = candidate.BaseScore
        });
    }

    private static void AddPriorityScore(
        MissionCandidate candidate)
    {
        var points = candidate.Priority?
            .Trim()
            .ToLowerInvariant() switch
        {
            "critical" => 40,
            "high" => 25,
            "normal" => 10,
            "low" => 5,
            _ => 0
        };

        if (points == 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Priority",

            Name = "Priority",

            Explanation =
                $"The work item has {candidate.Priority} priority.",

            Points = points
        });
    }

    private static void AddImmediateAttentionScore(
        MissionCandidate candidate)
    {
        var points = candidate.IsP1
            ? 200
            : candidate.RequiresImmediateAttention
                ? 100
                : 0;

        if (points == 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Immediate Attention",
            Name = candidate.IsP1 ? "P1 Incident" : "Immediate Attention",
            Explanation = candidate.IsP1
                ? "An assigned P1 incident takes precedence over routine planned work."
                : "The source system marked this item as requiring immediate attention.",
            Points = points
        });
    }
    private static void AddUrgencyScore(
        MissionCandidate candidate)
    {
        if (candidate.DueAt is null)
            return;

        var daysRemaining =
            (candidate.DueAt.Value.Date - DateTime.Today).Days;

        var points = daysRemaining switch
        {
            < 0 => 40,
            0 => 35,
            <= 2 => 25,
            <= 7 => 15,
            <= 14 => 5,
            _ => 0
        };

        if (points == 0)
            return;

        var explanation = daysRemaining switch
        {
            < 0 => "The work item is overdue.",
            0 => "The work item is due today.",
            1 => "The work item is due tomorrow.",
            _ => $"The work item is due in {daysRemaining} days."
        };

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Urgency",

            Name = "Urgency",

            Explanation = explanation,

            Points = points
        });
    }

    private static void AddBusinessValueScore(
        MissionCandidate candidate)
    {
        var points = CalculateBusinessValuePoints(candidate.BusinessValue);

        if (points == 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Business Value",

            Name = "Business Value",

            Explanation =
                $"Estimated business value is {candidate.BusinessValue:C0}.",

            Points = points
        });
    }

    private static void AddStrategicExposureScore(MissionCandidate candidate)
    {
        var points = candidate.StrategicBusinessValue switch
        {
            >= 1000000m => 25,
            >= 500000m => 18,
            >= 250000m => 12,
            >= 100000m => 7,
            _ => 0
        };
        if (points == 0) return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Strategic Exposure",
            Name = "Project Value Protected",
            Explanation = $"This next action protects a project carrying {candidate.StrategicBusinessValue:C0} total strategic value; it does not claim to deliver that full value itself.",
            Points = points
        });
    }

    internal static int CalculateBusinessValuePoints(decimal value)
    {
        if (value <= 0) return 0;
        if (value < 10000m) return 5;
        var points = 12 + (int)Math.Round(18 * Math.Log10((double)(value / 10000m)));
        return Math.Clamp(points, 12, 55);
    }

    private static void AddRiskScore(
        MissionCandidate candidate)
    {
        if (!candidate.AtRisk)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Risk",

            Name = "Risk",

            Explanation =
                "The work item carries elevated operational risk.",

            Points = 25
        });
    }

    private static void AddProgressScore(
        MissionCandidate candidate)
    {
        var progressPercent =
            (int)Math.Round(candidate.Progress * 100);

        var points = progressPercent switch
        {
            >= 90 and < 100 => 20,
            >= 70 => 12,
            >= 40 => 6,
            _ => 0
        };

        if (points == 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Progress",

            Name = "Near Completion",

            Explanation =
                $"The work item is already {progressPercent}% complete.",

            Points = points
        });
    }

    private static void AddEffortScore(
        MissionCandidate candidate)
    {
        var points = candidate.EstimatedMinutes switch
        {
            <= 15 => 12,
            <= 30 => 8,
            <= 60 => 4,
            _ => 0
        };

        if (points == 0)
            return;

        candidate.ScoreFactors.Add(new MissionScoreFactor
        {
            Category = "Effort",

            Name = "Quick Win",

            Explanation =
                $"Estimated effort is only {candidate.EstimatedMinutes} minutes.",

            Points = points
        });

        System.Diagnostics.Debug.WriteLine(
    $"{candidate.Title} -> Factors: {candidate.ScoreFactors.Count}");
    }
}