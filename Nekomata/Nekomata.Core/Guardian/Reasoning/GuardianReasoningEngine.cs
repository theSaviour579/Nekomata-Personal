using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Guardian.Reasoning;

public class GuardianReasoningEngine
{
    public IReadOnlyList<GuardianReason> BuildReasons(
        MissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var reasons = new List<GuardianReason>();

        BuildPriorityReasons(candidate, reasons);

        BuildBusinessReasons(candidate, reasons);

        BuildTimingReasons(candidate, reasons);

        BuildRiskReasons(candidate, reasons);

        return reasons;
    }

    private static void BuildPriorityReasons(
        MissionCandidate candidate,
        List<GuardianReason> reasons)
    {
        if (candidate.Priority == "Critical")
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Priority",
                    Title = "Critical Priority",
                    Explanation =
                        "This item has been marked as critical priority.",
                    Weight = 25,
                    Positive = true
                });
        }
        else if (candidate.Priority == "High")
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Priority",
                    Title = "High Priority",
                    Explanation =
                        "This item has been marked as high priority.",
                    Weight = 15,
                    Positive = true
                });
        }
    }

    private static void BuildBusinessReasons(
        MissionCandidate candidate,
        List<GuardianReason> reasons)
    {
        if (candidate.BusinessValue >= 10000)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Business",
                    Title = "High Business Value",
                    Explanation =
                        $"Estimated commercial value {candidate.BusinessValue:C0}.",
                    Weight = 20,
                    Positive = true
                });
        }
        else if (candidate.BusinessValue > 0)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Business",
                    Title = "Business Value",
                    Explanation =
                        $"Estimated business value {candidate.BusinessValue:C0}.",
                    Weight = 10,
                    Positive = true
                });
        }
    }

    private static void BuildTimingReasons(
        MissionCandidate candidate,
        List<GuardianReason> reasons)
    {
        if (candidate.DueAt is null)
            return;

        var remaining =
            candidate.DueAt.Value -
            DateTime.Now;

        if (remaining.TotalHours <= 2)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Timing",
                    Title = "Due Soon",
                    Explanation =
                        "Deadline falls within the next two hours.",
                    Weight = 12,
                    Positive = true
                });
        }
        else if (remaining.TotalDays <= 1)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Timing",
                    Title = "Due Today",
                    Explanation =
                        "This work should be completed today.",
                    Weight = 8,
                    Positive = true
                });
        }
    }

    private static void BuildRiskReasons(
        MissionCandidate candidate,
        List<GuardianReason> reasons)
    {
        if (candidate.AtRisk)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Risk",
                    Title = "Elevated Risk",
                    Explanation =
                        "Requires attention due to elevated operational risk.",
                    Weight = 10,
                    Positive = true
                });
        }

        foreach (var risk in candidate.Risks)
        {
            reasons.Add(
                new GuardianReason
                {
                    Category = "Risk",
                    Title = "Risk",
                    Explanation = risk,
                    Weight = 5,
                    Positive = false
                });
        }
    }
}