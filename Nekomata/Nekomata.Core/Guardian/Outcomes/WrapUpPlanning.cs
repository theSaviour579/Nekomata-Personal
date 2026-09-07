using Nekomata.Models.Guardian;
using Nekomata.Models.Tasks;

namespace Nekomata.Core.Guardian.Outcomes;

public static class WrapUpPlanning
{
    public static DateTime NextWorkday(DateTime date)
    {
        do { date = date.Date.AddDays(1); } while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return date;
    }

    public static bool IsDue(WrapUpObjective item, DateTime today) =>
        item.Decision == "Continue next workday" ? item.PlanDate.Date <= today.Date :
        item.Decision == "Defer until" && item.DeferUntil?.Date <= today.Date;

    public static List<WrapUpObjective> Build(IEnumerable<NekomataTask> tasks, DateTime today) => tasks
        .Where(x => !x.IsCompleted && !x.Completed &&
            (x.DueAt?.Date <= today.Date || x.IsScheduled && x.StartAt?.Date == today.Date))
        .DistinctBy(x => x.Id).OrderByDescending(x => x.PriorityScore)
        .Select(x => new WrapUpObjective { Key = $"task:{x.Id}", TaskId = x.Id, Title = x.Title, PlanDate = NextWorkday(today) }).ToList();
}
