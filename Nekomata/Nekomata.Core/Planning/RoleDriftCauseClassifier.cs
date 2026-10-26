using Nekomata.Core.Analytics;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Planning;

public enum RoleDriftCause
{
    UserSelected,
    UnavoidableIncident,
    CancelledMeetingOrUnavailableStakeholder,
    ExternallyImposedRequest,
    NekomataRecommended
}

public sealed record RoleDriftEvidence(RoleDriftCause Cause, double CorrectionWeight, string Explanation);

public sealed class RoleDriftCauseClassifier
{
    public RoleDriftEvidence Classify(WorkEpisode episode, IReadOnlyCollection<WorkdayEvent>? journal = null)
    {
        var text = $"{episode.Title} {episode.Category} {string.Join(' ', episode.Evidence)}".ToLowerInvariant();
        var related = (journal ?? []).Where(entry =>
            (episode.TaskId is long taskId && entry.TaskId == taskId) ||
            (episode.ProjectId is long projectId && entry.ProjectId == projectId) ||
            entry.OccurredAt.LocalDateTime >= episode.StartedAt.AddMinutes(-30) && entry.OccurredAt.LocalDateTime <= episode.FinishedAt.AddMinutes(30)).ToList();
        var relatedText = string.Join(' ', related.Select(entry => $"{entry.EventType} {entry.Category} {entry.Narrative}")).ToLowerInvariant();

        if (ContainsAny(text + " " + relatedText, "p1", "outage", "critical incident", "security incident", "unavoidable incident"))
            return new(RoleDriftCause.UnavoidableIncident, .2, "The episode coincides with an outage or critical-incident evidence; correction pressure is heavily damped.");
        if (ContainsAny(text + " " + relatedText, "meeting cancelled", "meeting canceled", "stakeholder unavailable", "organiser cancelled", "organizer canceled"))
            return new(RoleDriftCause.CancelledMeetingOrUnavailableStakeholder, .15, "Cancelled-meeting or unavailable-stakeholder evidence limits how strongly Guardian should correct the resulting gap.");
        if (related.Any(entry => entry.RecommendationId is not null || entry.EventType.Contains("Guardian", StringComparison.OrdinalIgnoreCase)) ||
            ContainsAny(text, "nekomata", "guardian recommended", "guardian planned"))
            return new(RoleDriftCause.NekomataRecommended, .85, "Nekomata appears to have recommended or prepared this work; the system retains most responsibility for the drift.");
        if (episode.Category is "Operational support" or "Administration & communication" ||
            ContainsAny(text + " " + relatedText, "external request", "email request", "teams request", "phone call", "customer request", "supplier request"))
            return new(RoleDriftCause.ExternallyImposedRequest, .55, "The evidence suggests an externally imposed request; correction remains useful but is deliberately less aggressive.");
        return new(RoleDriftCause.UserSelected, 1, "No unavoidable or external cause was evidenced, so the episode is treated as user-selected work.");
    }

    public static string ClassifyBand(string title, string sourceType = "")
    {
        var text = $"{title} {sourceType}".ToLowerInvariant();
        if (ContainsAny(text, "1:1", "one to one", "coaching", "mentor", "team leadership", "people management", "team review")) return "leadership";
        if (ContainsAny(text, "security", "compliance", "risk review", "governance", "service review", "operational review")) return "oversight";
        if (ContainsAny(text, "admin", "email", "chase", "timesheet")) return "admin";
        if (ContainsAny(text, "request", "query", "incident", "support", "urgent fix")) return "reactive";
        if (sourceType.Equals("Project", StringComparison.OrdinalIgnoreCase) ||
            ContainsAny(text, "strateg", "transformation", "automation", "implementation", "process improvement", "project")) return "strategic";
        return "technical";
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
}
