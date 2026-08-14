using Nekomata.Core.Guardian.Evidence;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Builders;

public class GuardianReasonBuilder
{
    public List<GuardianReason> Build(
        GuardianEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons =
            new List<GuardianReason>();

        if (!evidence.HasMission)
        {
            if (evidence.Undated > 0)
            {
                reasons.Add(new GuardianReason
                {
                    Category = "Planning",
                    Title = "Missing Due Dates",
                    Weight = -10,
                    Positive = false,
                    Explanation =
                        $"{evidence.Undated} open task(s) have no due date."
                });
            }

            return reasons;
        }

        reasons.Add(new GuardianReason
        {
            Category = "Business",
            Title = "High Business Value",
            Weight = 45,
            Positive = true,
            Explanation =
                $"Estimated value £{evidence.MissionBusinessValue:N0}."
        });

        reasons.Add(new GuardianReason
        {
            Category = "Priority",
            Title = "Highest Ranked Mission",
            Weight = evidence.MissionScore,
            Positive = true,
            Explanation =
                $"Guardian selected the highest scoring mission ({evidence.MissionScore})."
        });

        reasons.Add(new GuardianReason
        {
            Category = "Time",
            Title = "Estimated Effort",
            Weight = 15,
            Positive = true,
            Explanation =
                $"Estimated duration {evidence.MissionDuration.TotalMinutes:0} minutes."
        });

        if (!evidence.OverCapacity)
        {
            reasons.Add(new GuardianReason
            {
                Category = "Capacity",
                Title = "Capacity Available",
                Weight = 10,
                Positive = true,
                Explanation =
                    "Today's workload is within available capacity."
            });
        }
        else
        {
            reasons.Add(new GuardianReason
            {
                Category = "Capacity",
                Title = "Over Capacity",
                Weight = -25,
                Positive = false,
                Explanation =
    evidence.CapacitySummary,
            });
        }

        if (evidence.Undated > 0)
        {
            reasons.Add(new GuardianReason
            {
                Category = "Planning",
                Title = "Missing Due Dates",
                Weight = -10,
                Positive = false,
                Explanation =
                    $"{evidence.Undated} open task(s) have no due date."
            });
        }

        return reasons;
    }
}