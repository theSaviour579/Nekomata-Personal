using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianNarrativeBuilder
{
    public void Apply(
        NekomataWorkspace workspace,
        GuardianDecision decision)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(decision);

        var mission =
            workspace.CurrentMission;

        if (mission is null)
        {
            decision.Headline =
                "No urgent work requires immediate attention.";

            decision.Recommendation =
                BuildNoMissionRecommendation(
                    decision);

            return;
        }

        decision.Headline =
            $"Today's priority is {mission.Title}.";

        decision.Recommendation =
            BuildMissionRecommendation(
                mission.Title,
                mission.BusinessValue,
                mission.EstimatedDuration,
                decision);
    }

    private static string BuildMissionRecommendation(
        string title,
        decimal businessValue,
        TimeSpan estimatedDuration,
        GuardianDecision decision)
    {
        var recommendation =
            $"Begin with '{title}'.";

        if (businessValue > 0)
        {
            recommendation +=
                $" It protects approximately " +
                $"{businessValue:C0} of estimated business value.";
        }

        if (estimatedDuration > TimeSpan.Zero)
        {
            recommendation +=
                $" Allow around " +
                $"{FormatDuration(estimatedDuration)}.";
        }

        var criticalRisk =
            decision.Risks.FirstOrDefault(
                risk => risk.Critical);

        if (criticalRisk is not null)
        {
            recommendation +=
                $" Be aware: {criticalRisk.Description}";
        }

        return recommendation;
    }

    private static string BuildNoMissionRecommendation(
        GuardianDecision decision)
    {
        var topOpportunity =
            decision.Opportunities
                .OrderByDescending(
                    opportunity => opportunity.Priority)
                .ThenByDescending(
                    opportunity => opportunity.EstimatedValue)
                .FirstOrDefault();

        if (topOpportunity is not null)
        {
            return
                $"No primary mission is currently selected. " +
                $"The strongest available opportunity is " +
                $"'{topOpportunity.Title}'.";
        }

        var criticalRisk =
            decision.Risks.FirstOrDefault(
                risk => risk.Critical);

        if (criticalRisk is not null)
        {
            return
                $"No primary mission is currently selected. " +
                $"Address this risk first: " +
                $"{criticalRisk.Description}";
        }

        return
            "Use available capacity for strategic work, " +
            "planning or backlog reduction.";
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        var totalMinutes =
            Math.Max(
                (int)Math.Round(
                    duration.TotalMinutes),
                0);

        var hours =
            totalMinutes / 60;

        var minutes =
            totalMinutes % 60;

        if (hours == 0)
            return $"{minutes} minutes";

        if (minutes == 0)
            return hours == 1
                ? "1 hour"
                : $"{hours} hours";

        return $"{hours}h {minutes}m";
    }
}