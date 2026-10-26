namespace Nekomata.Core.Guardian.Neglect;

public enum NeglectKind { SlippingTask, AvoidedWork, UnbookedPromise, StaleProject, ReplyOwed }

public sealed record NeglectTask(long Id, string Title, DateTime? DueAt, int PriorityScore, bool IsScheduled,
    int RollForwardCount, DateTime? LastWorkedAt);

public sealed record NeglectProject(long Id, string Name, string Status, DateTime UpdatedAt, DateTime? LastWorkedAt,
    bool AtRisk, DateTime? DueAt);

public sealed record NeglectPromise(string Summary, string Counterparty, DateTimeOffset? DueAt, bool HasTimeBooked, string Source = "",
    Guid? CommitmentId = null);

public sealed record NeglectReply(string From, string Subject, DateTimeOffset ReceivedAt);

public sealed record NeglectItem(NeglectKind Kind, string Title, string Why, int Score, string Suggestion, long? TaskId = null)
{
    public NeglectPromise? Promise { get; init; }
    public IReadOnlyList<long> ProjectIds { get; init; } = [];
    public IReadOnlyList<string> ProjectNames { get; init; } = [];
}

/// <summary>
/// "What am I not doing?" - deterministic, from what Nekomata already holds: tasks that keep being rolled forward or
/// are overdue, the highest-value work nobody has touched, promises due soon with no time booked, active projects with
/// no activity, and emails from people still waiting on a reply. Scored so the most consequential comes first.
/// </summary>
public static class NeglectDetector
{
    public static IReadOnlyList<NeglectItem> Detect(
        DateTime now,
        IReadOnlyCollection<NeglectTask> tasks,
        IEnumerable<NeglectProject> projects,
        IEnumerable<NeglectPromise> promises,
        IEnumerable<NeglectReply> replies,
        int max = 10)
    {
        var items = new List<NeglectItem>();
        var today = now.Date;

        foreach (var task in tasks)
        {
            var overdueDays = task.DueAt is { } due && due.Date < today ? (today - due.Date).Days : 0;
            if (task.RollForwardCount >= 3 || overdueDays >= 2)
            {
                var reasons = new List<string>();
                if (task.RollForwardCount >= 3) reasons.Add($"rolled forward {task.RollForwardCount} times");
                if (overdueDays >= 2) reasons.Add($"{overdueDays} days overdue");
                var score = Math.Min(95, 45 + task.RollForwardCount * 8 + Math.Min(overdueDays, 10) * 4);
                items.Add(new(NeglectKind.SlippingTask, task.Title, Sentence(string.Join(" and ", reasons)), score,
                    task.RollForwardCount >= 5 ? "drop it or delegate it" : $"schedule {task.Title}", task.Id));
            }
        }

        // The top fifth by priority that nobody has worked on for a week and isn't booked in.
        var ranked = tasks.OrderByDescending(t => t.PriorityScore).ToList();
        var topCount = Math.Max(1, (int)Math.Ceiling(ranked.Count * 0.2));
        foreach (var task in ranked.Take(topCount))
        {
            if (task.IsScheduled || items.Any(i => i.TaskId == task.Id)) continue;
            var idleDays = task.LastWorkedAt is { } worked ? (today - worked.Date).Days : (int?)null;
            if (idleDays is < 7) continue;
            items.Add(new(NeglectKind.AvoidedWork, task.Title,
                idleDays is null ? "One of your highest-value tasks, and you haven't started it." : $"One of your highest-value tasks, untouched for {idleDays} days.",
                55, $"schedule {task.Title}", task.Id));
        }

        foreach (var promise in promises)
        {
            if (promise.HasTimeBooked || promise.DueAt is not { } due) continue;
            var hours = (due.LocalDateTime - now).TotalHours;
            // Due within two days, or overdue by up to a week. Past that, nothing has been heard about it for so long
            // that it has almost always been handled somewhere Nekomata can't see; it's reported as a count instead.
            if (hours > 48 || hours < -StalePromiseHours) continue;
            var to = string.IsNullOrWhiteSpace(promise.Counterparty) ? "" : $" to {promise.Counterparty}";
            var via = string.IsNullOrWhiteSpace(promise.Source) ? "" : $" ({promise.Source})";
            items.Add(new(NeglectKind.UnbookedPromise, promise.Summary,
                hours < 0 ? $"Promised{to}{via}, due {Ago(due.LocalDateTime, now)}, no time booked." : $"Due{to} {Due(due.LocalDateTime, now)}{via}, no time booked.",
                hours < 0 ? 92 : 80, "book time for it") { Promise = promise });
        }

        var stale = projects
            .Where(p => p.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .Select(p => (Project: p, Idle: (today - (p.LastWorkedAt is { } worked && worked > p.UpdatedAt ? worked : p.UpdatedAt).Date).Days))
            .Where(x => x.Idle >= 14)
            .OrderByDescending(x => x.Project.AtRisk).ThenByDescending(x => x.Idle)
            .ToList();
        if (stale.Count == 1)
        {
            var (project, idle) = stale[0];
            items.Add(new(NeglectKind.StaleProject, project.Name,
                $"No activity for {idle} days{(project.AtRisk ? ", and it's marked at risk" : "")}.",
                Math.Min(85, 35 + idle + (project.AtRisk ? 20 : 0)), "decide the next action, or put it on hold")
            { ProjectIds = [project.Id], ProjectNames = [project.Name] });
        }
        else if (stale.Count > 1)
        {
            // One line, not one per project: a list of eight identical "no activity" lines buries everything else.
            var names = stale.Select(x => x.Project.Name).ToList();
            var shown = names.Count <= 4 ? JoinNames(names) : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
            var atRisk = stale.Count(x => x.Project.AtRisk);
            var weeks = stale.Min(x => x.Idle) / 7;
            items.Add(new(NeglectKind.StaleProject, $"{stale.Count} active projects",
                $"Nothing on {shown} for {(weeks >= 2 ? $"{weeks}+ weeks" : "two weeks or more")}{(atRisk > 0 ? $"; {atRisk} marked at risk" : "")}.",
                Math.Min(70, 40 + stale.Count * 3 + atRisk * 10), "put them on hold, or name a next action")
            { ProjectIds = stale.Select(x => x.Project.Id).ToList(), ProjectNames = names });
        }

        foreach (var reply in replies)
        {
            var days = (today - reply.ReceivedAt.LocalDateTime.Date).Days;
            if (days < 2 || days > 14) continue;
            items.Add(new(NeglectKind.ReplyOwed, $"{reply.From}: {reply.Subject}",
                $"Waiting on your reply for {days} days.", Math.Min(80, 45 + days * 3), "reply, or say it doesn't need one"));
        }

        return items.OrderByDescending(i => i.Score).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
    }

    public const int StalePromiseHours = 7 * 24;

    private static string JoinNames(IReadOnlyList<string> names) =>
        names.Count <= 1 ? string.Join("", names) : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];

    private static string Ago(DateTime due, DateTime now) =>
        due.Date == now.Date ? $"at {due:HH:mm} today" : due.Date == now.Date.AddDays(-1) ? "yesterday" : $"{due:ddd d MMM}";

    private static string Sentence(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..] + ".";

    private static string Due(DateTime due, DateTime now) =>
        due.Date == now.Date ? $"today at {due:HH:mm}" : due.Date == now.Date.AddDays(1) ? "tomorrow" : due.ToString("dddd");
}
