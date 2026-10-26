using Nekomata.Models.Analytics;
using Nekomata.Models.Planning;

namespace Nekomata.Core.Analytics;

public static class LabourCostCalculator
{
    public static TimeSpan GetElapsedPaidWorkday(DateTime now, WorkingDaySettings settings)
    {
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return TimeSpan.Zero;

        var start = settings.GetStart(now);
        var end = settings.GetEnd(now);
        var cappedNow = now < start ? start : now > end ? end : now;
        if (cappedNow <= start || end <= start) return TimeSpan.Zero;

        var elapsed = cappedNow - start;
        if (!settings.IncludeLunchBreak) return elapsed;

        var lunchStart = settings.GetLunchStart(now);
        var lunchEnd = settings.GetLunchEnd(now);
        var excludedStart = start > lunchStart ? start : lunchStart;
        var excludedEnd = cappedNow < lunchEnd ? cappedNow : lunchEnd;
        if (excludedEnd > excludedStart) elapsed -= excludedEnd - excludedStart;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public static void ApplyElapsedWorkdayCost(
        MissionAnalytics analytics,
        DateTime now,
        WorkingDaySettings settings,
        LabourCostSettings costing)
    {
        var elapsed = GetElapsedPaidWorkday(now, settings);
        var rate = Math.Max(0, costing.HourlyLabourRate);
        var wageCost = Math.Round((decimal)elapsed.TotalHours * rate, 2, MidpointRounding.AwayFromZero);

        analytics.ElapsedPaidWorkdayToday = elapsed;
        analytics.HourlyLabourRate = rate;
        analytics.WageCostToday = wageCost;
        analytics.ValueAfterWageCostToday = analytics.BusinessValueDeliveredToday - wageCost;
        analytics.BusinessValueToWageCostRatio = wageCost > 0
            ? Math.Round(analytics.BusinessValueDeliveredToday / wageCost, 2)
            : 0;
    }
}
