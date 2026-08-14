using Nekomata.Models.Guardian;

namespace Nekomata.Core.Missions.Suggestions;

public interface ISuggestedMissionProvider
{
    string Name { get; }

    IReadOnlyList<GuardianInsight> GetInsights(
        SuggestedMissionContext context);
}