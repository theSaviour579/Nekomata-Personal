using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Briefing;

public sealed class GuardianContextBriefingService
{
    public GuardianContextBriefing Build(GuardianContextBriefingInput input)
    {
        if (input.IsBusy)
            return Waiting(input.Area, "REFRESHING", "Guardian is reconciling the latest evidence.");
        if (!input.IsLoaded)
            return Waiting(input.Area, "AWAITING EVIDENCE", $"Open or refresh {input.Area} to build this briefing.");

        var material = input.Signals
            .Where(x => x.Significance >= 20)
            .OrderByDescending(x => x.IsActionable)
            .ThenByDescending(x => x.Significance)
            .ThenByDescending(x => x.ChangedAt)
            .ToList();
        var actionable = material.Count(x => x.IsActionable);
        if (material.Count == 0)
            return new()
            {
                Area = input.Area, StateLabel = "ALL CLEAR", ConfidencePercent = 92,
                Headline = $"Nothing in {input.Area} currently needs intervention.",
                Summary = RefreshSummary(input.LastRefreshed), Evidence = "No material or actionable signals met the contextual threshold."
            };

        var lead = material[0];
        return new()
        {
            Area = input.Area,
            StateLabel = actionable > 0 ? $"{actionable} ACTIONABLE" : "CONTEXT ONLY",
            Headline = lead.Title,
            Summary = actionable > 0
                ? $"{actionable} item{Plural(actionable)} need action; {material.Count - actionable} additional signal{Plural(material.Count - actionable)} retained as context."
                : $"{material.Count} material change{Plural(material.Count)} detected; none currently require intervention.",
            NextAction = lead.RecommendedAction,
            Evidence = lead.Evidence,
            ContextId = lead.ContextId,
            ConfidencePercent = Math.Clamp(55 + lead.Significance / 2, 60, 96),
            HasAction = lead.IsActionable && !string.IsNullOrWhiteSpace(lead.RecommendedAction)
        };
    }

    private static GuardianContextBriefing Waiting(string area, string state, string summary) => new()
    {
        Area = area, StateLabel = state, Headline = "Building the live contextual briefing…", Summary = summary
    };
    private static string RefreshSummary(DateTimeOffset? refreshed) => refreshed is null
        ? "The latest available evidence is clear."
        : $"Checked {refreshed.Value.LocalDateTime:HH:mm}; the latest available evidence is clear.";
    private static string Plural(int count) => count == 1 ? "" : "s";
}
