using Nekomata.Core.Missions.Suggestions.Rules;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Missions.Suggestions;

public class SuggestedMissionProvider
    : ISuggestedMissionProvider
{
    private readonly IEnumerable<ISuggestedMissionRule>
        _rules;

    public string Name =>
        "Guardian Suggestions";

    public SuggestedMissionProvider(
        IEnumerable<ISuggestedMissionRule> rules)
    {
        _rules = rules;
    }

    public IReadOnlyList<GuardianInsight> GetInsights(
        SuggestedMissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var insights =
            _rules
                .SelectMany(rule =>
                {
                    var results =
                        rule.Evaluate(context);

                    System.Diagnostics.Debug.WriteLine(
                        $"Guardian Rule: {rule.Name} " +
                        $"generated {results.Count} insight(s).");

                    return results;
                })
                .GroupBy(
                    insight => insight.Id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group.First())
                .ToList();

        System.Diagnostics.Debug.WriteLine(
            $"Guardian generated {insights.Count} insight(s).");

        return insights;
    }
}