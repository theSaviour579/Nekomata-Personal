using Nekomata.Core.Analytics.Capacity;
using Xunit;

namespace Nekomata.Tests;

public sealed class CalendarCapacityIntervalTests
{
    [Fact]
    public void Overlapping_events_are_counted_once()
    {
        var day = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var offset = TimeSpan.Zero;
        var workStart = new DateTimeOffset(day.AddHours(8), offset);
        var workEnd = new DateTimeOffset(day.AddHours(16.5), offset);
        var intervals = new[]
        {
            (new DateTimeOffset(day.AddHours(9), offset), new DateTimeOffset(day.AddHours(11), offset)),
            (new DateTimeOffset(day.AddHours(10), offset), new DateTimeOffset(day.AddHours(12), offset))
        };

        var result = CalendarCapacityIntervalCalculator.Calculate(intervals, workStart, workEnd);

        Assert.Single(result);
        Assert.Equal(180, CalendarCapacityIntervalCalculator.TotalMinutes(result));
    }

    [Fact]
    public void Events_are_clipped_to_workday_and_lunch_is_excluded()
    {
        var day = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified);
        var offset = TimeSpan.Zero;
        var workStart = new DateTimeOffset(day.AddHours(8), offset);
        var workEnd = new DateTimeOffset(day.AddHours(16.5), offset);
        var lunchStart = new DateTimeOffset(day.AddHours(12.5), offset);
        var lunchEnd = new DateTimeOffset(day.AddHours(13.5), offset);
        var intervals = new[]
        {
            (new DateTimeOffset(day.AddHours(7), offset), new DateTimeOffset(day.AddHours(14), offset)),
            (new DateTimeOffset(day.AddHours(16), offset), new DateTimeOffset(day.AddHours(18), offset))
        };

        var result = CalendarCapacityIntervalCalculator.Calculate(
            intervals, workStart, workEnd, lunchStart, lunchEnd);

        Assert.Equal(330, CalendarCapacityIntervalCalculator.TotalMinutes(result));
    }
}