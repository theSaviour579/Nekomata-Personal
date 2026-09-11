using Nekomata.AI.Interfaces;
using Nekomata.Integrations.MicrosoftGraph.Profile;
using Nekomata.Models.Analytics;

namespace Nekomata.UI.Services;

public sealed class PersonalRoleProfileService(
    PersonalProfileService profile,
    IMicrosoftUserProfileService microsoftProfile,
    IAIProvider ai)
{
    public async Task<PersonalRoleProfile> ResolveAsync(bool regenerate = false, CancellationToken cancellationToken = default)
    {
        var title = profile.Current.JobTitle.Trim();
        var source = profile.Current.JobTitleSource;
        try
        {
            var microsoft = await microsoftProfile.GetAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(microsoft?.JobTitle) &&
                (title.Length == 0 || source.Equals("Microsoft 365", StringComparison.OrdinalIgnoreCase)))
            {
                title = microsoft.JobTitle;
                source = "Microsoft 365";
            }
        }
        catch { /* A missing profile claim must not make Value unavailable. */ }

        if (title.Length == 0) title = "Professional";
        var current = profile.Current.RoleProfile;
        if (!regenerate && current is { Goals.Count: > 0 } && current.JobTitle.Equals(title, StringComparison.OrdinalIgnoreCase)) return current;

        var goals = await GenerateGoalsAsync(title);
        var resolved = new PersonalRoleProfile { JobTitle = title, Source = source, GeneratedAt = DateTimeOffset.UtcNow, Goals = goals };
        profile.SaveRoleProfile(resolved);
        return resolved;
    }

    private async Task<List<RoleGoalDefinition>> GenerateGoalsAsync(string jobTitle)
    {
        try
        {
            var result = await ai.AskJsonAsync<RoleGoalProposal>($$"""
                Create four practical outcome goals for a person whose job title is "{{jobTitle}}".
                Goals must describe the normal scope of that role and work for any employer. Do not mention internal products, ticketing systems, or technologies unless the job title itself requires them.
                For every goal return: key, name, description, minPercent, maxPercent, and 5-10 lowercase keywords used to recognise matching work.
                Allocation ranges should collectively describe a balanced working week. Each minPercent must be 5-45 and each maxPercent 10-60.
                Return JSON as { "goals": [...] }.
                """);
            var valid = Validate(result?.Goals);
            if (valid.Count >= 3) return valid.Take(6).ToList();
        }
        catch { /* Deterministic role templates keep the feature available without AI. */ }
        return BuildFallback(jobTitle);
    }

    private static List<RoleGoalDefinition> Validate(IEnumerable<RoleGoalDefinition>? goals) => goals?.Where(x =>
            !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Description) &&
            x.MinPercent is >= 0 and <= 100 && x.MaxPercent is >= 1 and <= 100 && x.MinPercent <= x.MaxPercent && x.Keywords.Count > 0)
        .Select((x, index) => x with { Key = string.IsNullOrWhiteSpace(x.Key) ? $"goal-{index + 1}" : x.Key.Trim(), Name = x.Name.Trim(), Description = x.Description.Trim(), Keywords = x.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).Distinct().Take(12).ToList() }).ToList() ?? [];

    internal static List<RoleGoalDefinition> BuildFallback(string jobTitle)
    {
        var lower = jobTitle.ToLowerInvariant();
        var specialist = lower.Contains("sales") || lower.Contains("account") ?
            Goal("growth", "Customer growth", "Develop opportunities and move customer commitments forward.", 25, 40, "customer", "client", "proposal", "pipeline", "sale", "account") :
            lower.Contains("marketing") ? Goal("reach", "Audience and growth", "Create campaigns and improve audience response.", 25, 40, "campaign", "content", "audience", "brand", "channel", "lead") :
            lower.Contains("project") || lower.Contains("programme") ? Goal("delivery", "Programme delivery", "Move milestones, dependencies and agreed outcomes forward.", 30, 45, "milestone", "project", "delivery", "dependency", "plan", "scope") :
            lower.Contains("finance") || lower.Contains("accountant") ? Goal("stewardship", "Financial stewardship", "Maintain accurate reporting, controls and sound decisions.", 30, 45, "budget", "forecast", "invoice", "finance", "report", "control") :
            lower.Contains("people") || lower.Contains("human resource") || lower.Contains("recruit") ? Goal("people", "People outcomes", "Support performance, development and a healthy employee experience.", 30, 45, "people", "employee", "hiring", "development", "performance", "culture") :
            lower.Contains("analyst") || lower.Contains("data") ? Goal("insight", "Insight and analysis", "Turn evidence into clear findings and useful decisions.", 30, 45, "analysis", "data", "insight", "report", "research", "measure") :
            lower.Contains("teacher") || lower.Contains("education") ? Goal("learning", "Learning outcomes", "Plan, deliver and assess effective learning.", 35, 50, "lesson", "student", "learning", "assessment", "curriculum", "teach") :
            lower.Contains("manager") || lower.Contains("lead") || lower.Contains("head") ? Goal("leadership", "Team leadership", "Set direction, remove blockers and develop people.", 25, 40, "team", "coach", "decision", "priority", "stakeholder", "lead") :
            Goal("outcomes", "Core role outcomes", $"Deliver the main outcomes expected of a {jobTitle}.", 30, 45, "deliver", "complete", "customer", "quality", "outcome", "project");
        return
        [
            specialist,
            Goal("service", "People and service", "Build trust and respond well to the people who rely on your work.", 15, 30, "meeting", "support", "respond", "stakeholder", "customer", "colleague"),
            Goal("improvement", "Improvement", "Improve quality, capability and how work gets done.", 10, 25, "improve", "learn", "review", "process", "quality", "develop"),
            Goal("planning", "Planning and coordination", "Keep priorities, commitments and follow-through under control.", 15, 30, "plan", "prepare", "coordinate", "schedule", "follow", "admin")
        ];
    }

    private static RoleGoalDefinition Goal(string key, string name, string description, int min, int max, params string[] keywords) => new() { Key = key, Name = name, Description = description, MinPercent = min, MaxPercent = max, Keywords = [.. keywords] };
    private sealed class RoleGoalProposal { public List<RoleGoalDefinition> Goals { get; init; } = []; }
}
