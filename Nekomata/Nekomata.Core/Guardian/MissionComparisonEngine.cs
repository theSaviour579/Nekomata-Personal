using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Guardian;

public class MissionComparisonEngine
{
    public IReadOnlyList<MissionComparisonReason> Compare(
        Mission winner,
        Mission alternative)
    {
        ArgumentNullException.ThrowIfNull(winner);
        ArgumentNullException.ThrowIfNull(alternative);

        var categories =
            winner.ScoreFactors
                .Select(factor => factor.Category)
                .Concat(
                    alternative.ScoreFactors
                        .Select(factor => factor.Category))
                .Where(category =>
                    !string.IsNullOrWhiteSpace(category))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var reasons =
            categories
                .Select(category =>
                    BuildCategoryComparison(
                        winner,
                        alternative,
                        category))
                .OrderByDescending(reason =>
                    Math.Abs(reason.Difference))
                .ThenBy(reason =>
                    reason.Category)
                .ToList();

        return reasons;
    }

    private static MissionComparisonReason
        BuildCategoryComparison(
            Mission winner,
            Mission alternative,
            string category)
    {
        var winnerPoints =
            winner.ScoreFactors
                .Where(factor =>
                    string.Equals(
                        factor.Category,
                        category,
                        StringComparison.OrdinalIgnoreCase))
                .Sum(factor =>
                    factor.Points);

        var alternativePoints =
            alternative.ScoreFactors
                .Where(factor =>
                    string.Equals(
                        factor.Category,
                        category,
                        StringComparison.OrdinalIgnoreCase))
                .Sum(factor =>
                    factor.Points);

        var difference =
            winnerPoints -
            alternativePoints;

        return new MissionComparisonReason
        {
            Category =
                category,

            WinnerPoints =
                winnerPoints,

            AlternativePoints =
                alternativePoints,

            Explanation =
                BuildExplanation(
                    category,
                    difference)
        };
    }

    private static string BuildExplanation(
        string category,
        int difference)
    {
        if (difference > 0)
        {
            return
                $"{category} contributed " +
                $"{difference} additional point" +
                $"{(difference == 1 ? "" : "s")} " +
                "to the selected mission.";
        }

        if (difference < 0)
        {
            var alternativeAdvantage =
                Math.Abs(difference);

            return
                $"{category} favoured the alternative by " +
                $"{alternativeAdvantage} point" +
                $"{(alternativeAdvantage == 1 ? "" : "s")}.";
        }

        return
            $"{category} was weighted equally.";
    }
}