using Nekomata.Models.Guardian;

namespace Nekomata.Core.Missions.Suggestions.Rules;

public interface ISuggestedMissionRule
{
    string Name { get; }

    IReadOnlyList<GuardianInsight> Evaluate(
        SuggestedMissionContext context);
}