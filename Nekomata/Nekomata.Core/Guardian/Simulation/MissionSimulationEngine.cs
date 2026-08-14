using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Simulation;

public class MissionSimulationEngine
{
    public MissionSimulation Simulate(
        Mission currentMission,
        GuardianRejectedMission alternative,
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(currentMission);
        ArgumentNullException.ThrowIfNull(alternative);
        ArgumentNullException.ThrowIfNull(workspace);

        var simulation = new MissionSimulation
        {
            MissionTitle = alternative.Title,

            Score = alternative.Score,
            ScoreDifference =
                alternative.Score - currentMission.Score,

            BusinessValue = alternative.BusinessValue,
            BusinessValueDifference =
                alternative.BusinessValue -
                currentMission.BusinessValue,

            EstimatedMinutes =
                alternative.EstimatedMinutes,

            EstimatedMinutesDifference =
                alternative.EstimatedMinutes -
                (int)currentMission.EstimatedDuration.TotalMinutes,

            Confidence =
                workspace.Guardian.Confidence,

            ConfidenceDifference = 0
        };

        BuildConsequences(
            simulation,
            currentMission,
            alternative,
            workspace);

        return simulation;
    }

    private static void BuildConsequences(
        MissionSimulation simulation,
        Mission currentMission,
        GuardianRejectedMission alternative,
        NekomataWorkspace workspace)
    {
        // Higher business value
        if (simulation.BusinessValueDifference > 0)
        {
            simulation.Benefits.Add(
                $"Potentially increases business value by approximately {simulation.BusinessValueDifference:C0}.");
        }
        else if (simulation.BusinessValueDifference < 0)
        {
            simulation.Consequences.Add(
                $"Reduces estimated business value by approximately {Math.Abs(simulation.BusinessValueDifference):C0}.");
        }

        // Score
        if (simulation.ScoreDifference > 0)
        {
            simulation.Benefits.Add(
                $"Scores {simulation.ScoreDifference} points higher than the current recommendation.");
        }
        else if (simulation.ScoreDifference < 0)
        {
            simulation.Consequences.Add(
                $"Scores {Math.Abs(simulation.ScoreDifference)} points lower than Guardian's recommendation.");
        }

        // Duration
        if (simulation.EstimatedMinutesDifference > 0)
        {
            simulation.Consequences.Add(
                $"Requires approximately {simulation.EstimatedMinutesDifference} extra minutes.");
        }
        else if (simulation.EstimatedMinutesDifference < 0)
        {
            simulation.Benefits.Add(
                $"Could save approximately {Math.Abs(simulation.EstimatedMinutesDifference)} minutes.");
        }

        // Due date
        if (alternative.DueAt is null)
        {
            simulation.Consequences.Add(
                "Has no due date, making it less time-sensitive.");
        }
        else if (alternative.DueAt <= DateTime.Now.AddDays(1))
        {
            simulation.Benefits.Add(
                "Should be completed soon due to its deadline.");
        }

        // Capacity
        var remaining =
            workspace.Briefing.RemainingCapacityMinutes;

        if (alternative.EstimatedMinutes > remaining)
        {
            simulation.ExceedsCapacity = true;

            simulation.CapacityImpactMinutes =
                alternative.EstimatedMinutes - remaining;

            simulation.Consequences.Add(
                $"Would exceed today's capacity by approximately {simulation.CapacityImpactMinutes} minutes.");
        }

        simulation.RiskLevel =
            simulation.ExceedsCapacity
                ? "High"
                : simulation.ScoreDifference < -10
                    ? "Medium"
                    : "Low";

        simulation.RecommendSwitch =
       simulation.ScoreDifference > 0 &&
       !simulation.ExceedsCapacity;

        if (simulation.RecommendSwitch)
        {
            simulation.Verdict =
                $"Switch to {simulation.MissionTitle}. " +
                $"It provides a stronger overall recommendation " +
                $"while remaining within today's available capacity.";
        }
        else if (simulation.ExceedsCapacity)
        {
            simulation.Verdict =
                $"Keep the current mission. " +
                $"{simulation.MissionTitle} would exceed today's " +
                $"available capacity by approximately " +
                $"{simulation.CapacityImpactMinutes} minutes.";
        }
        else if (simulation.BusinessValueDifference < 0)
        {
            simulation.Verdict =
                $"Keep the current mission. " +
                $"Switching would reduce estimated business value " +
                $"by approximately " +
                $"{Math.Abs(simulation.BusinessValueDifference):C0}.";
        }
        else if (simulation.ScoreDifference < 0)
        {
            simulation.Verdict =
                $"Keep the current mission. " +
                $"{simulation.MissionTitle} scores " +
                $"{Math.Abs(simulation.ScoreDifference)} points lower " +
                $"than Guardian's current recommendation.";
        }
        else
        {
            simulation.Verdict =
                $"Both missions are viable. " +
                $"Guardian currently favours " +
                $"{currentMission.Title}, but either objective " +
                $"could reasonably be completed today.";
        }
    }
}