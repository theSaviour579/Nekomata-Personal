using Nekomata.Models.Planning;

namespace Nekomata.Core.Planning;

/// <summary>
/// Working hours, weekends and England &amp; Wales bank holidays: when Guardian should be off duty, and how long the
/// last break was (so the first morning back can open with a catch-up).
/// </summary>
public static class OffHours
{
    public static bool IsWorkingDay(DateTime date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !IsBankHoliday(date);

    /// <summary>True outside the working day: weekends, bank holidays, before the start or after the end of hours.</summary>
    public static bool IsOutOfHours(DateTime local, WorkingDaySettings settings) =>
        !IsWorkingDay(local.Date) || local < settings.GetStart(local.Date) || local >= settings.GetEnd(local.Date);

    /// <summary>The end of the previous working day before <paramref name="local"/>'s day.</summary>
    public static DateTime PreviousWorkingDayEnd(DateTime local, WorkingDaySettings settings)
    {
        var day = local.Date.AddDays(-1);
        for (var guard = 0; guard < 14 && !IsWorkingDay(day); guard++) day = day.AddDays(-1);
        return settings.GetEnd(day);
    }

    /// <summary>"the weekend", "the bank holiday weekend", "the bank holiday", or "" when yesterday was a working day.</summary>
    public static string BreakName(DateTime today)
    {
        var days = new List<DateTime>();
        for (var day = today.Date.AddDays(-1); days.Count < 14 && !IsWorkingDay(day); day = day.AddDays(-1)) days.Add(day);
        if (days.Count == 0) return "";
        var holiday = days.Any(IsBankHoliday);
        var weekend = days.Any(d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return (holiday, weekend) switch
        {
            (true, true) => "the bank holiday weekend",
            (true, false) => days.Count > 1 ? "the holidays" : "the bank holiday",
            _ => "the weekend"
        };
    }

    public static bool IsBankHoliday(DateTime date)
    {
        date = date.Date;
        var y = date.Year;
        var easter = EasterSunday(y);
        if (date == easter.AddDays(-2) || date == easter.AddDays(1)) return true;
        if (date == FirstMonday(y, 5) || date == LastMonday(y, 5) || date == LastMonday(y, 8)) return true;
        if (date == Substitute(new DateTime(y, 1, 1))) return true;

        // Christmas and Boxing Day: a weekend day moves to the next free weekday.
        var christmas = new DateTime(y, 12, 25);
        var boxing = new DateTime(y, 12, 26);
        var christmasObserved = Substitute(christmas);
        var boxingObserved = Substitute(boxing);
        if (boxingObserved <= christmasObserved) boxingObserved = christmasObserved.AddDays(1);
        while (boxingObserved.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) boxingObserved = boxingObserved.AddDays(1);
        return date == christmasObserved || date == boxingObserved;
    }

    private static DateTime Substitute(DateTime day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => day.AddDays(2),
        DayOfWeek.Sunday => day.AddDays(1),
        _ => day
    };

    private static DateTime FirstMonday(int year, int month)
    {
        var day = new DateTime(year, month, 1);
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(1);
        return day;
    }

    private static DateTime LastMonday(int year, int month)
    {
        var day = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(-1);
        return day;
    }

    // Anonymous Gregorian algorithm.
    public static DateTime EasterSunday(int year)
    {
        var a = year % 19; var b = year / 100; var c = year % 100;
        var d = b / 4; var e = b % 4; var f = (b + 8) / 25; var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30; var i = c / 4; var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7; var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31; var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }
}
