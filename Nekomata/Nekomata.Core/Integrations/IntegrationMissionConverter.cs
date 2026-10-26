using Nekomata.Core.Guardian.Reasoning;
using Nekomata.Models.Common;
using Nekomata.Models.Integrations;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Integrations;

public class IntegrationMissionConverter
{
    private readonly GuardianReasoningEngine
        _reasoningEngine;

    public IntegrationMissionConverter(
     GuardianReasoningEngine reasoningEngine)
    {
        _reasoningEngine =
            reasoningEngine;
    }

    public MissionCandidate Convert(
        IntegrationMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);

        var candidate = new MissionCandidate
        {
            SourceType = mission.SourceType,

            SourceRecordId = mission.SourceRecordId,

            ExternalUrl = mission.ExternalUrl,

            Title = mission.Title,

            Description = mission.Description,

            Priority =
                string.IsNullOrWhiteSpace(mission.Priority)
                    ? TaskPriorities.Normal
                    : mission.Priority,

            EstimatedMinutes =
                Math.Max(mission.EstimatedMinutes, 1),

            BusinessValue =
                mission.BusinessValue,

            DueAt =
                mission.DueAt,

            LastUpdatedAt = mission.LastUpdatedAt,

            ExternalStatusId = mission.ExternalStatusId,

            IsAwaitingExternalResponse = mission.IsAwaitingExternalResponse,

            IsActionable = mission.IsActionable,

            AtRisk =
                mission.CustomerImpact ||
                mission.RequiresImmediateAttention,

            RequiresImmediateAttention = mission.RequiresImmediateAttention,

            IsUrgent = mission.IsUrgent,
            SuggestedOwner = mission.SuggestedOwner,
            CurrentOwner = mission.CurrentOwner,
            AssignmentConfidence = mission.AssignmentConfidence,
            AssignmentReason = mission.AssignmentReason
        };

        if (mission.CustomerImpact)
        {
            candidate.Strengths.Add(
                "Direct customer impact");
        }

        if (mission.RevenueImpact)
        {
            candidate.Strengths.Add(
                "Revenue affecting");
        }

        if (mission.IsAwaitingExternalResponse)
        {
            candidate.Strengths.Add(mission.IsActionable
                ? "External response is stale and needs chasing"
                : $"Awaiting external response; last updated {mission.LastUpdatedAt:dd MMM HH:mm}");
        }

        if (mission.EstimatedMinutes > 120)
        {
            candidate.Risks.Add(
                "Long running activity");
        }

        var reasons =
            _reasoningEngine.BuildReasons(candidate);

        candidate.GuardianReasons.AddRange(reasons);

        candidate.RecommendationReason =
            candidate.GuardianReasons.Count > 0
                ? string.Join(
                    " ",
                    candidate.GuardianReasons
                        .Select(reason => reason.Explanation))
                : $"Imported from {mission.SourceType}.";

        return candidate;
    }
}
