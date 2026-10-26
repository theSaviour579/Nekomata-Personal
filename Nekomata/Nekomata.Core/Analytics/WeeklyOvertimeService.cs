using System.Text.Json;
using Nekomata.Data.Repositories;
using Nekomata.Core.Missions;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using Nekomata.Models.Planning;

namespace Nekomata.Core.Analytics;

public sealed record OvertimeDay(DateTime Date, int Minutes);

public sealed record WeeklyOvertimeSummary(
    DateTime WeekStarting, DateTime Through, int TotalMinutes,
    int BeforeHoursMinutes, int LunchMinutes, int AfterHoursMinutes, int WeekendMinutes,
    decimal EquivalentCost, int RecordedSessions, IReadOnlyList<OvertimeDay> Days)
{
    public string TotalLabel => FormatMinutes(TotalMinutes);
    public static string FormatMinutes(int minutes) => minutes switch
    {
        <= 0 => "0m",
        < 60 => $"{minutes}m",
        _ => $"{minutes / 60}h {minutes % 60:00}m"
    };
}

public sealed class WeeklyOvertimeService
{
    private readonly IMissionSessionRepository _sessions;
    private readonly WorkingDaySettings _workingDay;
    private readonly LabourCostSettings _costing;

    public WeeklyOvertimeService(IMissionSessionRepository sessions, WorkingDaySettings workingDay, LabourCostSettings costing)
    {
        _sessions = sessions;
        _workingDay = workingDay;
        _costing = costing;
    }

    public async Task<WeeklyOvertimeSummary> GetCurrentWeekAsync(DateTime now, IEnumerable<MissionWorkSegment>? activeSegments = null)
    {
        var monday = now.Date.AddDays(-((7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7));
        var through = now < monday.AddDays(7) ? now : monday.AddDays(7);
        var sessions = (await _sessions.GetBetweenAsync(monday, monday.AddDays(7)))
            .Where(item => !item.Cancelled && item.ActualDurationMinutes > 0).ToList();
        var portions = new List<WeightedInterval>();
        foreach (var session in sessions) AddSessionIntervals(portions, session, monday, through);
        foreach (var segment in activeSegments ?? []) AddInterval(portions, segment.Start, segment.End, 1d, monday, through);

        var totals = new Dictionary<DateTime, double>();
        double before = 0, lunch = 0, after = 0, weekend = 0;
        foreach (var portion in portions)
        {
            var cursor = portion.Start;
            while (cursor < portion.End)
            {
                var dayEnd = cursor.Date.AddDays(1);
                var end = portion.End < dayEnd ? portion.End : dayEnd;
                var date = cursor.Date;
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    Add((end - cursor).TotalMinutes * portion.Weight, ref weekend, totals, date);
                else
                {
                    AddOverlap(cursor, end, date, date, _workingDay.GetStart(date), portion.Weight, ref before, totals);
                    if (_workingDay.IncludeLunchBreak)
                        AddOverlap(cursor, end, date, _workingDay.GetLunchStart(date), _workingDay.GetLunchEnd(date), portion.Weight, ref lunch, totals);
                    AddOverlap(cursor, end, date, _workingDay.GetEnd(date), dayEnd, portion.Weight, ref after, totals);
                }
                cursor = end;
            }
        }

        var total = Round(before + lunch + after + weekend);
        return new WeeklyOvertimeSummary(monday, through, total, Round(before), Round(lunch), Round(after), Round(weekend),
            Math.Round(total / 60m * Math.Max(0, _costing.HourlyLabourRate), 2, MidpointRounding.AwayFromZero), sessions.Count,
            totals.OrderBy(item => item.Key).Select(item => new OvertimeDay(item.Key, Round(item.Value))).Where(item => item.Minutes > 0).ToList());
    }

    public int GetAdditionalMinutes(MissionSession session, DateTime from, DateTime to)
    {
        var portions = new List<WeightedInterval>();
        AddSessionIntervals(portions, session, from, to);
        return Round(portions.Sum(GetAdditionalMinutes));
    }

    private double GetAdditionalMinutes(WeightedInterval portion)
    {
        double result = 0;
        var cursor = portion.Start;
        while (cursor < portion.End)
        {
            var dayEnd = cursor.Date.AddDays(1);
            var end = portion.End < dayEnd ? portion.End : dayEnd;
            if (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                result += (end - cursor).TotalMinutes * portion.Weight;
            else
            {
                result += Overlap(cursor, end, cursor.Date, _workingDay.GetStart(cursor), portion.Weight);
                if (_workingDay.IncludeLunchBreak)
                    result += Overlap(cursor, end, _workingDay.GetLunchStart(cursor), _workingDay.GetLunchEnd(cursor), portion.Weight);
                result += Overlap(cursor, end, _workingDay.GetEnd(cursor), dayEnd, portion.Weight);
            }
            cursor = end;
        }
        return result;
    }

    private static double Overlap(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd, double weight)
    {
        var overlapStart = start > windowStart ? start : windowStart;
        var overlapEnd = end < windowEnd ? end : windowEnd;
        return overlapEnd > overlapStart ? (overlapEnd - overlapStart).TotalMinutes * weight : 0;
    }

    private static void AddSessionIntervals(List<WeightedInterval> target, MissionSession session, DateTime from, DateTime to)
    {
        var stored = ReadIntervals(session.ActiveIntervalsJson);
        if (stored.Count > 0)
        {
            foreach (var item in stored) AddInterval(target, item.Start, item.End, 1d, from, to);
            return;
        }
        var elapsed = (session.FinishedAt - session.StartedAt).TotalMinutes;
        if (elapsed <= 0) return;
        AddInterval(target, session.StartedAt, session.FinishedAt,
            Math.Clamp(session.ActualDurationMinutes / elapsed, 0d, 1d), from, to);
    }

    private static List<MissionWorkSegment> ReadIntervals(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<MissionWorkSegment>>(json) ?? []; }
        catch { return []; }
    }

    private static void AddInterval(List<WeightedInterval> target, DateTime start, DateTime end, double weight, DateTime from, DateTime to)
    {
        start = start < from ? from : start;
        end = end > to ? to : end;
        if (end > start && weight > 0) target.Add(new(start, end, weight));
    }

    private static void AddOverlap(DateTime start, DateTime end, DateTime date, DateTime windowStart, DateTime windowEnd,
        double weight, ref double category, Dictionary<DateTime, double> totals)
    {
        var overlapStart = start > windowStart ? start : windowStart;
        var overlapEnd = end < windowEnd ? end : windowEnd;
        if (overlapEnd <= overlapStart) return;
        Add((overlapEnd - overlapStart).TotalMinutes * weight, ref category, totals, date);
    }

    private static void Add(double minutes, ref double category, Dictionary<DateTime, double> totals, DateTime date)
    {
        category += minutes;
        totals[date] = totals.GetValueOrDefault(date) + minutes;
    }

    private static int Round(double value) => Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    private sealed record WeightedInterval(DateTime Start, DateTime End, double Weight);
}
