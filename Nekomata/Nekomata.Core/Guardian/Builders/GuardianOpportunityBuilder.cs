using Nekomata.Core.Guardian.Evidence;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using System.Security.Policy;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianOpportunityBuilder
{
    public List<GuardianOpportunity> Build(
        GuardianEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var opportunities =
            new List<GuardianOpportunity>();

        if (evidence.MissionBusinessValue >= 10000)
        {
            opportunities.Add(new GuardianOpportunity
            {
                Title = "High Business Value",

                Description =
                    $"Estimated opportunity worth approximately £{evidence.MissionBusinessValue:N0}.",

                EstimatedValue =
                    (int)evidence.MissionBusinessValue,

                Priority =
                    evidence.MissionScore,

                Category = "Business"
            });
        }

        if (evidence.MissionDuration.TotalMinutes <= 15 &&
            evidence.HasMission)
        {
            opportunities.Add(new GuardianOpportunity
            {
                Title = "Quick Win",

                Description =
                    "The selected mission can be completed quickly.",

                EstimatedValue = 0,

                Priority = 20,

                Category = "Quick Win"
            });
        }

        if (!evidence.OverCapacity &&
            evidence.HasMission)
        {
            opportunities.Add(new GuardianOpportunity
            {
                Title = "Available Capacity",

                Description =
                    "Today's workload has room for additional strategic work.",

                EstimatedValue = 0,

                Priority = 15,

                Category = "Capacity"
            });
        }

        return opportunities
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.EstimatedValue)
            .ToList();
    }
}