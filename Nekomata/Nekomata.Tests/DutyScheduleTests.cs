using Nekomata.Core.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class DutyScheduleTests
{
    private static readonly DateTime Today = new(2026, 10, 17);

    [Fact]
    public void Monthly_after_last_done_and_clamped()
    {
        Assert.Equal(new DateTime(2026, 11, 25), DutySchedule.NextDue(new Duty { Frequency = "monthly", Day = 25, LastDone = new DateTime(2026, 10, 24) }, Today));
        Assert.Equal(new DateTime(2026, 10, 25), DutySchedule.NextDue(new Duty { Frequency = "monthly", Day = 25 }, Today));
        Assert.Equal(new DateTime(2026, 11, 30), DutySchedule.NextDue(new Duty { Frequency = "monthly", Day = 31, LastDone = new DateTime(2026, 10, 31) }, Today));
    }

    [Fact]
    public void Never_done_is_due_from_this_period_so_overdue_shows()
    {
        var due = DutySchedule.NextDue(new Duty { Frequency = "monthly", Day = 5 }, Today);
        Assert.Equal(new DateTime(2026, 10, 5), due);   // overdue
    }

    [Fact]
    public void Weekly_quarterly_yearly_and_days()
    {
        Assert.Equal(new DateTime(2026, 10, 19), DutySchedule.NextDue(new Duty { Frequency = "weekly", Day = 1, LastDone = new DateTime(2026, 10, 12) }, Today));
        Assert.Equal(new DateTime(2027, 1, 15), DutySchedule.NextDue(new Duty { Frequency = "quarterly", Day = 15, Month = 1, LastDone = new DateTime(2026, 10, 15) }, Today));
        Assert.Equal(new DateTime(2026, 11, 20), DutySchedule.NextDue(new Duty { Frequency = "quarterly", Day = 20, Month = 2, LastDone = new DateTime(2026, 8, 20) }, Today));
        Assert.Equal(new DateTime(2027, 3, 15), DutySchedule.NextDue(new Duty { Frequency = "yearly", Month = 3, Day = 15, LastDone = new DateTime(2026, 3, 15) }, Today));
        Assert.Equal(new DateTime(2026, 11, 1), DutySchedule.NextDue(new Duty { Frequency = "days", EveryDays = 30, LastDone = new DateTime(2026, 10, 2) }, Today));
    }
}
