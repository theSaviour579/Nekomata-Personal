using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Data.Repositories;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private Guid? _activeRecommendationId;
    private Guid? _activeCorrelationId;
    private Guid? _activeDecisionEventId;
    private Task RecordWorkdayEventAsync(WorkdayEvent entry) =>
        _services.GetRequiredService<WorkdayJournalService>().RecordAsync(entry);

    private Task RecordDayStartedAsync() =>
        _services.GetRequiredService<WorkdayJournalService>().RecordOncePerDayAsync(new WorkdayEvent
        {
        EventType = "DayStarted", Category = "Planning", Title = "Day started", Significance = 65,
        Narrative = $"{Workspace.Capacity.ScheduledMinutesToday / 60d:0.#}h calendar commitments. Guardian identified {Workspace.CurrentMission.Title} as the strongest available focus objective.",
        TaskId = Workspace.CurrentMission.TaskId, ProjectId = Workspace.CurrentMission.ProjectId,
        Recommendation = $"Start {Workspace.CurrentMission.Title}"
        });

    private async Task RecordRecommendationDecisionAsync(string recommendation, string decision, string instead = "", string metadata = "{}")
    {
        var chain = await RecordDecisionChainAsync(recommendation, decision, instead, metadata);
        _activeRecommendationId = chain.CorrelationId;
        _activeCorrelationId = chain.CorrelationId;
        _activeDecisionEventId = chain.DecisionEventId;
    }

    private async Task<(Guid CorrelationId, Guid DecisionEventId)> RecordDecisionChainAsync(
        string recommendation, string decision, string instead = "", string metadata = "{}")
    {
        var correlationId = Guid.NewGuid();
        var recommendationEventId = Guid.NewGuid();
        var decisionEventId = Guid.NewGuid();
        await RecordWorkdayEventAsync(new WorkdayEvent
        {
            EventId = recommendationEventId, CorrelationId = correlationId,
            EventType = "Recommendation", Category = "Guardian", Title = "Guardian recommended",
            Narrative = recommendation, RecommendationId = correlationId, Recommendation = recommendation,
            Outcome = "Awaiting decision", Significance = 65, Metadata = metadata
        });
        await RecordWorkdayEventAsync(new WorkdayEvent
        {
            EventId = decisionEventId, CorrelationId = correlationId,
            ParentEventId = recommendationEventId, CausedByEventId = recommendationEventId,
            EventType = "RecommendationDecision", Category = "Guardian", Title = "Recommendation decision",
            Narrative = string.IsNullOrWhiteSpace(instead) ? $"Guardian recommended: {recommendation}. User: {decision}." :
                $"Guardian recommended: {recommendation}. User: {decision}. User instead: {instead}.",
            RecommendationId = correlationId, Recommendation = recommendation, UserDecision = decision,
            Outcome = "Pending", Significance = 70, Metadata = metadata
        });
        return (correlationId, decisionEventId);
    }

    private async Task<string> AddJournalContextAsync(string message, string originalQuery)
    {
        var lower = originalQuery.ToLowerInvariant();
        if (!new[] { "last ", "this week", "this month", "when did", "why didn't", "why did", "derailed", "accomplished", "history", "timeline" }.Any(lower.Contains))
            return message;
        var entries = await _services.GetRequiredService<IWorkdayEventRepository>().SearchAsync(originalQuery, 30);
        if (entries.Count == 0) return message + "\n\nWORKDAY JOURNAL: No matching durable events were found.";
        var journal = string.Join("\n", entries.OrderBy(x => x.OccurredAt).Select(x =>
            $"- {x.OccurredAt.LocalDateTime:ddd dd MMM yyyy HH:mm} | {x.Title} | {x.Narrative} | Decision: {x.UserDecision} | Outcome: {x.Outcome}"));
        return $"{message}\n\nDURABLE WORKDAY JOURNAL (prefer this immutable event history over reconstructing mutable current state):\n{journal}";
    }
}
