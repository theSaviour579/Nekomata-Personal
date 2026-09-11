using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;

namespace Nekomata.UI.Services;

public sealed class PersonalValueService(IMissionSessionRepository sessions, IProjectRepository projects)
{
    public async Task<PersonalValueSummary> BuildAsync(PersonalRoleProfile role, DateTime now)
    {
        var start = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var completed = (await sessions.GetBetweenAsync(start, start.AddDays(7))).Where(x => x.Completed && !x.Cancelled).OrderByDescending(x => x.FinishedAt).ToList();
        var projectNames = (await projects.GetAllAsync()).ToDictionary(x => x.Id, x => $"{x.Name} {x.Description} {x.NextAction}");
        var buckets = role.Goals.ToDictionary(x => x.Key, _ => 0);
        var episodes = new List<PersonalWorkEpisode>();
        var aligned = 0;
        foreach (var session in completed)
        {
            var text = $"{session.Title} {session.SourceType} {(session.ProjectId is long id && projectNames.TryGetValue(id, out var project) ? project : "")}".ToLowerInvariant();
            var goal = role.Goals.Select(x => new { Goal = x, Matches = x.Keywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) }).OrderByDescending(x => x.Matches).FirstOrDefault();
            var minutes = Math.Max(0, session.ActualDurationMinutes);
            if (goal is { Matches: > 0 }) { buckets[goal.Goal.Key] += minutes; aligned += minutes; }
            episodes.Add(new() { Title = session.Title, GoalName = goal is { Matches: > 0 } ? goal.Goal.Name : "Unaligned work", Minutes = minutes, BusinessValue = session.BusinessValue, FinishedAt = session.FinishedAt });
        }
        var total = episodes.Sum(x => x.Minutes);
        var progress = role.Goals.Select(x => new RoleGoalProgress { Name = x.Name, Description = x.Description, Minutes = buckets[x.Key], ActualPercent = total == 0 ? 0 : (int)Math.Round(buckets[x.Key] * 100d / total), MinPercent = x.MinPercent, MaxPercent = x.MaxPercent }).ToList();
        var insights = new List<string>();
        if (total == 0) insights.Add("Complete or log a focus session to start measuring role alignment.");
        else
        {
            var under = progress.Where(x => x.ActualPercent < x.MinPercent).OrderBy(x => x.ActualPercent - x.MinPercent).FirstOrDefault();
            if (under is not null) insights.Add($"{under.Name} is below its suggested range; consider protecting time for it next.");
            var unaligned = total - aligned;
            if (unaligned > 0) insights.Add($"{unaligned} minutes did not match a role goal. Review the goal keywords if that work belongs in your role.");
            if (insights.Count == 0) insights.Add("This week's recorded work is balanced across the role goals currently in range.");
        }
        return new PersonalValueSummary { JobTitle = role.JobTitle, RoleSource = role.Source, PeriodLabel = $"{start:dd MMM}–{start.AddDays(6):dd MMM}", TotalMinutes = total, DeliveredValue = completed.Sum(x => x.BusinessValue), AlignmentPercent = total == 0 ? 0 : (int)Math.Round(aligned * 100d / total), Goals = progress, Episodes = episodes.Take(12).ToList(), Insights = insights };
    }
}
