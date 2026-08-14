using Nekomata.Models.Common;
using Nekomata.Models.Integrations;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Integrations.Halo;

public class HaloMissionPolicy
    : IHaloMissionPolicy
{
    public void Apply(
        IntegrationMission mission,
        MissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(candidate);

        ApplyPriority(mission, candidate);

        ApplyMetrics(mission, candidate);

        ApplyGuardianReasons(mission, candidate);
    }

    private static void ApplyPriority(
        IntegrationMission mission,
        MissionCandidate candidate)
    {
        candidate.Priority = mission.Priority;
    }

    private static void ApplyMetrics(
        IntegrationMission mission,
        MissionCandidate candidate)
    {
        // Preserve the genuine business metric.
        candidate.BusinessValue =
            mission.BusinessValue;

        // Operational metrics.
        candidate.Urgency =
            CalculateUrgency(mission);

        candidate.RiskScore =
            CalculateRisk(mission);
    }

    private static int CalculateUrgency(
        IntegrationMission mission)
    {
        if (mission.IsAwaitingExternalResponse)
            return mission.IsActionable ? 40 : 0;

        var urgency = 0;

        if (mission.SlaExpiresAt is not null)
        {
            var remaining =
                mission.SlaExpiresAt.Value -
                DateTime.Now;

            if (remaining.TotalMinutes <= 0)
            {
                urgency += 100;
            }
            else if (remaining.TotalMinutes <= 60)
            {
                urgency += 90;
            }
            else if (remaining.TotalHours <= 4)
            {
                urgency += 70;
            }
            else if (remaining.TotalDays <= 1)
            {
                urgency += 40;
            }
        }

        if (mission.Priority == TaskPriorities.Critical)
        {
            urgency += 20;
        }
        else if (mission.Priority == TaskPriorities.High)
        {
            urgency += 10;
        }

        return Math.Clamp(urgency, 0, 100);
    }

    private static int CalculateRisk(
        IntegrationMission mission)
    {
        var risk = 0;

        if (mission.SecurityRelated)
        {
            risk += 50;
        }

        if (mission.CustomerImpact)
        {
            risk += 25;
        }

        if (mission.RevenueImpact)
        {
            risk += 15;
        }

        return Math.Clamp(risk, 0, 100);
    }

    private static void ApplyGuardianReasons(
        IntegrationMission mission,
        MissionCandidate candidate)
    {
        if (mission.CustomerImpact)
        {
            candidate.Strengths.Add(
                "Customer affecting");
        }

        if (mission.SecurityRelated)
        {
            candidate.Strengths.Add(
                "Security related");
        }

        if (!mission.IsAwaitingExternalResponse && mission.SlaExpiresAt is not null)
        {
            var remaining =
                mission.SlaExpiresAt.Value -
                DateTime.Now;

            if (remaining.TotalMinutes <= 60)
            {
                candidate.Strengths.Add(
                    "SLA expires within one hour");
            }
            else if (remaining.TotalHours <= 2)
            {
                candidate.Strengths.Add(
                    "SLA approaching");
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"Halo Policy -> {candidate.Title} | " +
            $"Priority={candidate.Priority} | IsP1={candidate.IsP1} | Immediate={candidate.RequiresImmediateAttention} | " +
            $"BusinessValue={candidate.BusinessValue:C0} | " +
            $"Urgency={candidate.Urgency} | " +
            $"Risk={candidate.RiskScore} | " +
            $"Strengths={string.Join(", ", candidate.Strengths)}");
    }
}