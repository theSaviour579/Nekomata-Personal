using Nekomata.AI.Interfaces;
using Nekomata.Models.People;

namespace Nekomata.UI.Services;

public sealed class TeamRoleSkillService(IAIProvider ai)
{
    public async Task<IReadOnlyList<TeamMemberSkill>> SuggestAsync(string role)
    {
        role = string.IsNullOrWhiteSpace(role) ? "Professional" : role.Trim();
        try
        {
            var response = await ai.AskJsonAsync<SkillResponse>($$"""
                Suggest 5 to 8 practical skills for the job title "{{role}}".
                Use employer-neutral capabilities. Do not mention organisation-specific systems.
                Return JSON as { "skills": [{ "name": "...", "proficiency": 1-4, "isPrimary": true/false, "aliases": "comma-separated recognition terms" }] }.
                """);
            var valid = response?.Skills.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => new TeamMemberSkill
            {
                Name = x.Name.Trim(), Proficiency = Math.Clamp(x.Proficiency, 1, 4), IsPrimary = x.IsPrimary,
                Aliases = string.Join(", ", x.Aliases.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            }).DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? [];
            if (valid.Count >= 3) return valid;
        }
        catch { }
        return Fallback(role);
    }

    internal static IReadOnlyList<TeamMemberSkill> Fallback(string role)
    {
        var lower = role.ToLowerInvariant();
        var specialist = lower.Contains("sales") || lower.Contains("account") ? Skill("Customer development", true, "customer, client, proposal, pipeline") :
            lower.Contains("project") || lower.Contains("programme") ? Skill("Project delivery", true, "project, milestone, dependency, delivery") :
            lower.Contains("finance") || lower.Contains("accountant") ? Skill("Financial analysis", true, "budget, forecast, reporting, control") :
            lower.Contains("marketing") ? Skill("Campaign development", true, "campaign, audience, content, brand") :
            lower.Contains("people") || lower.Contains("human resource") ? Skill("People development", true, "employee, performance, coaching, hiring") :
            lower.Contains("analyst") || lower.Contains("data") ? Skill("Analysis and insight", true, "analysis, data, evidence, insight") :
            lower.Contains("manager") || lower.Contains("lead") || lower.Contains("head") ? Skill("Leadership", true, "leadership, coaching, delegation, priorities") :
            Skill("Role delivery", true, "delivery, quality, outcomes, improvement");
        return [specialist, Skill("Communication", true, "communication, presentation, writing, listening"), Skill("Planning", false, "planning, coordination, prioritisation, scheduling"), Skill("Collaboration", false, "teamwork, stakeholder, partnership, support"), Skill("Problem solving", false, "problem, decision, investigation, solution")];
    }

    private static TeamMemberSkill Skill(string name, bool primary, string aliases) => new() { Name = name, Proficiency = primary ? 3 : 2, IsPrimary = primary, Aliases = aliases };
    private sealed class SkillResponse { public List<SkillSuggestion> Skills { get; init; } = []; }
    private sealed class SkillSuggestion { public string Name { get; init; } = ""; public int Proficiency { get; init; } = 2; public bool IsPrimary { get; init; } public string Aliases { get; init; } = ""; }
}
