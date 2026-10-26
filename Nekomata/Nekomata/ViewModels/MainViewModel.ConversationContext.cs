namespace Nekomata.UI.ViewModels;

// A deliberately small piece of tracked state: the single most recent thing a
// command actually surfaced to the user (an attention item, a ranked mission
// candidate, or a decision explanation), so a bare conversational follow-up such
// as "why?" or "why is that first?" has something concrete to resolve against.
// This is NOT a general conversation-memory system: it holds exactly one entity,
// it expires after a short window, and "AttentionItem"/"MissionCandidate" are
// always re-resolved live by key rather than trusted as a cached snapshot - only
// "Explanation" replays pre-rendered text, because Chronicle decisions aren't
// cheaply re-queryable by key.
public sealed record GuardianLastSurfacedEntity(
    string EntityType, string Key, string Label, string SourceAction, string Explanation, DateTimeOffset SurfacedAt);

public partial class MainViewModel
{
    private static readonly TimeSpan LastSurfacedEntityTimeout = TimeSpan.FromMinutes(10);
    private GuardianLastSurfacedEntity? _lastSurfacedEntity;

    private void TrackSurfacedEntity(string entityType, string key, string label, string sourceAction, string explanation = "") =>
        _lastSurfacedEntity = new(entityType, key, label, sourceAction, explanation, DateTimeOffset.Now);

    // Bare follow-ups ("why?", "why is that first?") land here. The guardrail the user
    // asked for: never guess. If nothing was surfaced recently, or the thing that was
    // surfaced is gone (resolved, no longer ranked), say so plainly instead of
    // inventing an answer.
    private void ExplainLastSurfacedEntity()
    {
        var entity = _lastSurfacedEntity;
        if (entity is null || DateTimeOffset.Now - entity.SurfacedAt > LastSurfacedEntityTimeout)
        {
            ShowPaletteResponse("I'm not sure what you're referring to. Ask me to show your attention items, your next objective, or a recommendation first, then ask why.");
            return;
        }

        switch (entity.EntityType)
        {
            case "AttentionItem":
            {
                var item = AttentionItems.FirstOrDefault(x =>
                    x.Key.Equals(entity.Key, StringComparison.OrdinalIgnoreCase) && x.IsActive);
                ShowPaletteResponse(item is null
                    ? $"\"{entity.Label}\" is no longer active on the Attention list."
                    : $"Why \"{item.Title}\" is at the top\n{item.JudgementReason}\n\n{item.Detail}");
                return;
            }
            case "MissionCandidate":
            {
                var candidate = Workspace.RankedMissionCandidates.FirstOrDefault(x =>
                    string.Equals(Nekomata.Core.Missions.NextActionPolicy.Key(x), entity.Key, StringComparison.OrdinalIgnoreCase));
                if (candidate is null)
                {
                    ShowPaletteResponse($"\"{entity.Label}\" is no longer in the ranked list.");
                    return;
                }
                var factors = candidate.ScoreFactors.OrderByDescending(x => Math.Abs(x.Points)).Take(5)
                    .Select(x => $"• {x.Name}: {x.Points:+#;-#;0}");
                ShowPaletteResponse($"Why {candidate.Title} is recommended\nScore {candidate.Score} · rank {candidate.Rank}\n{candidate.RecommendationReason}\n" +
                    string.Join("\n", factors));
                return;
            }
            default:
                ShowPaletteResponse(entity.Explanation.Length > 0
                    ? entity.Explanation
                    : "I'm not sure what you're referring to. Ask me to show something first, then ask why.");
                return;
        }
    }
}
