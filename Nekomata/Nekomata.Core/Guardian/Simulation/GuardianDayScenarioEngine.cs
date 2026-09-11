using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Simulation;

public sealed class GuardianDayScenarioEngine
{
    public IReadOnlyList<GuardianDayScenario> Build(GuardianDayScenarioInput input)
    {
        var baseline = BuildOne(input, "current", "Current ranking", "Follow Guardian's present priority order.", null, null);
        var protectedPlan = BuildOne(input, "protect", "Protect selected objective", "Put the selected objective first, then reflow the remaining work.", input.PreferredTaskId, null);
        var earlyPlan = BuildOne(input, "leave", $"Leave by {(input.LeaveTodayAt ?? input.WorkdayEnd):hh\\:mm}", "Cap today's working window and carry lower-ranked work forward.", null, input.LeaveTodayAt ?? input.WorkdayEnd);
        baseline.ComparisonSummary = "Baseline used to measure the other options.";
        AddComparison(protectedPlan, baseline);
        AddComparison(earlyPlan, baseline);
        return [baseline, protectedPlan, earlyPlan];
    }

    private static GuardianDayScenario BuildOne(GuardianDayScenarioInput input, string key, string title, string subtitle, long? preferredTaskId, TimeSpan? todayEnd)
    {
        var scenario = new GuardianDayScenario { Key = key, Title = title, Subtitle = subtitle, ConfidencePercent = Confidence(input) };
        var horizonDays = Math.Clamp(input.HorizonWorkingDays, 1, 10);
        var days = WorkingDays(input.Now.Date, horizonDays).ToList();
        var gaps = days.SelectMany(day => FreeWindows(input, day, todayEnd)).ToList();
        var ordered = input.WorkItems.OrderByDescending(x => preferredTaskId == x.TaskId).ThenByDescending(x => x.IsCritical)
            .ThenBy(x => x.Rank <= 0 ? int.MaxValue : x.Rank).ThenByDescending(x => x.Score).ThenBy(x => x.DueAt).ToList();

        foreach (var item in ordered)
        {
            var remaining = Math.Max(input.MinimumBlockMinutes, item.Minutes);
            DateTimeOffset? completion = null;
            foreach (var gap in gaps.Where(x => x.End > x.Start))
            {
                if (remaining <= 0) break;
                var available = Math.Max(0, (int)(gap.End - gap.Start).TotalMinutes);
                if (available < Math.Min(input.MinimumBlockMinutes, remaining)) continue;
                var minutes = Math.Min(Math.Min(120, remaining), available);
                var start = gap.Start;
                var end = start.AddMinutes(minutes);
                scenario.Blocks.Add(new() { TaskId = item.TaskId, Title = item.Title, Start = start, End = end });
                gap.Start = end;
                remaining -= minutes;
                completion = end;
            }
            scenario.ScheduledMinutes += Math.Max(0, item.Minutes - remaining);
            if (remaining > 0)
            {
                scenario.UnscheduledMinutes += remaining;
                scenario.ThreatenedCommitments.Add($"{item.Title} · {remaining} min could not be placed");
                if (item.IsCritical) scenario.StakeholderActions.Add($"Prepare an update for '{item.Title}' before accepting this plan.");
            }
            else if (item.DueAt is DateTime due && completion?.LocalDateTime > due)
                scenario.ThreatenedCommitments.Add($"{item.Title} · completes after its {due:ddd HH:mm} due time");
            if (!string.IsNullOrWhiteSpace(item.ProjectName) && (remaining > 0 || item.DueAt is DateTime projectDue && completion?.LocalDateTime > projectDue))
                scenario.ProjectConsequences.Add($"{item.ProjectName}: delivery confidence reduces if '{item.Title}' slips.");
        }
        scenario.ProjectedFinishAt = scenario.Blocks.Count == 0 ? null : scenario.Blocks.Max(x => x.End);
        scenario.Assumptions.Add("Task estimates represent active work and may differ from elapsed time.");
        scenario.Assumptions.Add("Fixed calendar commitments and lunch are protected; focus work may be split into blocks of up to two hours.");
        scenario.Evidence.Add($"{input.WorkItems.Count} ranked work items · {input.Commitments.Count} calendar commitments · {horizonDays}-working-day horizon.");
        scenario.Evidence.Add(input.CalendarLoaded ? "Microsoft 365 calendar evidence was loaded for the comparison." : "Calendar evidence was incomplete, so this result is provisional.");
        scenario.ProjectConsequences = scenario.ProjectConsequences.Distinct().Take(5).ToList();
        scenario.StakeholderActions = scenario.StakeholderActions.Distinct().Take(5).ToList();
        return scenario;
    }

    private static IEnumerable<MutableWindow> FreeWindows(GuardianDayScenarioInput input, DateTime day, TimeSpan? todayEnd)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(day);
        var start = new DateTimeOffset(day.Date.Add(input.WorkdayStart), offset);
        var endTime = day.Date == input.Now.Date && todayEnd is not null ? todayEnd.Value : input.WorkdayEnd;
        var end = new DateTimeOffset(day.Date.Add(endTime), offset);
        if (day.Date == input.Now.Date && input.Now > start) start = RoundUp(input.Now, 5);
        if (end <= start) yield break;
        var occupied = input.Commitments.Where(x => x.Start.Date <= day.Date && x.End.Date >= day.Date)
            .Select(x => x.IsAllDay ? (Start: start, End: end) : (x.Start, x.End)).ToList();
        if (input.IncludeLunch)
        {
            var lunchStart = new DateTimeOffset(day.Date.Add(input.LunchStart), offset);
            occupied.Add((lunchStart, lunchStart.AddMinutes(input.LunchMinutes)));
        }
        var windows = new List<MutableWindow> { new(start, end) };
        foreach (var busy in occupied.OrderBy(x => x.Start)) windows = windows.SelectMany(x => Subtract(x, busy.Start, busy.End)).ToList();
        foreach (var window in windows.Where(x => x.End > x.Start)) yield return window;
    }

    private static IEnumerable<MutableWindow> Subtract(MutableWindow window, DateTimeOffset busyStart, DateTimeOffset busyEnd)
    {
        if (busyEnd <= window.Start || busyStart >= window.End) { yield return window; yield break; }
        if (busyStart > window.Start) yield return new(window.Start, busyStart);
        if (busyEnd < window.End) yield return new(busyEnd, window.End);
    }

    private static void AddComparison(GuardianDayScenario scenario, GuardianDayScenario baseline)
    {
        var original = baseline.Blocks.GroupBy(x => x.TaskId).ToDictionary(x => x.Key, x => new { x.First().Title, Finish = x.Max(y => y.End), Minutes = x.Sum(y => y.Minutes) });
        var changed = scenario.Blocks.GroupBy(x => x.TaskId).ToDictionary(x => x.Key, x => new { x.First().Title, Finish = x.Max(y => y.End), Minutes = x.Sum(y => y.Minutes) });
        foreach (var item in original)
        {
            if (!changed.TryGetValue(item.Key, out var replacement)) scenario.DisplacedCommitments.Add($"{item.Value.Title} moves outside this scenario's horizon.");
            else if (replacement.Minutes < item.Value.Minutes) scenario.DisplacedCommitments.Add($"{item.Value.Title} loses {item.Value.Minutes - replacement.Minutes} planned minutes.");
            else if (replacement.Finish.Date > item.Value.Finish.Date || replacement.Finish - item.Value.Finish >= TimeSpan.FromMinutes(30)) scenario.DisplacedCommitments.Add($"{replacement.Title} moves from {item.Value.Finish:ddd HH:mm} to {replacement.Finish:ddd HH:mm}.");
        }
        scenario.DisplacedCommitments = scenario.DisplacedCommitments.Take(6).ToList();
        var newlyThreatened = Math.Max(0, scenario.ThreatenedCommitments.Count - baseline.ThreatenedCommitments.Count);
        scenario.MaterialDifferenceCount = scenario.DisplacedCommitments.Count + newlyThreatened;
        scenario.ComparisonSummary = scenario.MaterialDifferenceCount == 0 ? "No material change from the current ranking." : $"Compared with current ranking: {scenario.DisplacedCommitments.Count} objective(s) move later" + (newlyThreatened > 0 ? $", with {newlyThreatened} additional commitment(s) threatened." : ".");
    }

    private static int Confidence(GuardianDayScenarioInput input) => Math.Clamp(45 + (input.CalendarLoaded ? 25 : 0) + Math.Min(15, input.WorkItems.Count(x => x.Minutes > 0) * 2), 45, 92);
    private static IEnumerable<DateTime> WorkingDays(DateTime start, int count)
    {
        for (var date = start.Date; count > 0; date = date.AddDays(1)) if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) { yield return date; count--; }
    }
    private static DateTimeOffset RoundUp(DateTimeOffset value, int minutes) { var ticks = TimeSpan.FromMinutes(minutes).Ticks; return new DateTimeOffset((value.Ticks + ticks - 1) / ticks * ticks, value.Offset); }
    private sealed class MutableWindow(DateTimeOffset start, DateTimeOffset end) { public DateTimeOffset Start { get; set; } = start; public DateTimeOffset End { get; } = end; }
}
