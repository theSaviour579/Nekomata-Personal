using Nekomata.Core.Planning;
using Nekomata.Models.Planning;
using Xunit;

namespace Nekomata.Tests;

public sealed class OffHoursTests
{
    private static readonly WorkingDaySettings Hours = new() { StartTime = new(8, 0, 0), EndTime = new(16, 30, 0) };

    [Theory]
    [InlineData("2026-04-03")] // Good Friday
    [InlineData("2026-04-06")] // Easter Monday
    [InlineData("2026-05-04")] // Early May
    [InlineData("2026-05-25")] // Spring
    [InlineData("2026-08-31")] // Summer
    [InlineData("2026-12-25")]
    [InlineData("2026-12-28")] // Boxing Day (Saturday) substitute
    [InlineData("2027-12-27")] // Christmas on Saturday -> Monday
    [InlineData("2027-12-28")] // Boxing Day on Sunday -> Tuesday
    [InlineData("2028-01-03")] // New Year on Saturday -> Monday
    public void Knows_bank_holidays(string date) => Assert.True(OffHours.IsBankHoliday(DateTime.Parse(date)));

    [Theory]
    [InlineData("2026-10-16 09:00", false)] // Friday morning
    [InlineData("2026-10-16 16:30", true)]  // end of the day
    [InlineData("2026-10-16 07:59", true)]
    [InlineData("2026-10-17 11:00", true)]  // Saturday
    [InlineData("2026-12-25 10:00", true)]
    public void Out_of_hours(string at, bool expected) =>
        Assert.Equal(expected, OffHours.IsOutOfHours(DateTime.Parse(at), Hours));

    [Fact]
    public void Monday_looks_back_to_friday_and_names_the_break()
    {
        var monday = new DateTime(2026, 10, 19, 8, 30, 0);
        Assert.Equal(new DateTime(2026, 10, 16, 16, 30, 0), OffHours.PreviousWorkingDayEnd(monday, Hours));
        Assert.Equal("the weekend", OffHours.BreakName(monday.Date));
        Assert.Equal("the bank holiday weekend", OffHours.BreakName(new DateTime(2026, 5, 26)));
        Assert.Equal("", OffHours.BreakName(new DateTime(2026, 10, 20)));
    }
}
