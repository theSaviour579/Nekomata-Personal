namespace Nekomata.Core.Planning;

/// <summary>A recurring duty. Frequency: weekly (Day = 1 Mon..7 Sun), monthly (Day of month; 31 = last day),
/// quarterly (Day of the first month of each quarter, or Month = 2/3 for later in the quarter), yearly (Month, Day),
/// days (every EveryDays days from the last time it was done).</summary>
public sealed class Duty
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Frequency { get; set; } = "monthly";
    public int Day { get; set; } = 1;
    public int Month { get; set; } = 1;
    public int EveryDays { get; set; } = 30;
    /// <summary>Days of warning before it's due.</summary>
    public int LeadDays { get; set; } = 3;
    public DateTime? LastDone { get; set; }
    /// <summary>Reserved for evidence Guardian can see that counts as doing it. Empty = tick by hand.</summary>
    public string Evidence { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public static class DutySchedule
{
    /// <summary>The next due date: the first scheduled date after it was last done (or this period's date if never done).</summary>
    public static DateTime NextDue(Duty duty, DateTime today)
    {
        today = today.Date;
        // Done a few days early counts for that due date (capped at a third of the period, so a weekly duty done on time
        // doesn't skip the next week).
        int Grace(int periodDays) => Math.Max(0, Math.Min(duty.LeadDays, periodDays / 3));
        var done = duty.LastDone?.Date;
        switch (duty.Frequency.ToLowerInvariant())
        {
            case "days":
                return done is { } d0 ? d0.AddDays(Math.Max(1, duty.EveryDays)) : today;
            case "weekly":
            {
                var target = (DayOfWeek)(Math.Clamp(duty.Day, 1, 7) % 7);
                var start = done is { } l ? l.AddDays(1 + Grace(7)) : today.AddDays(-6);
                var d = start;
                while (d.DayOfWeek != target) d = d.AddDays(1);
                return d;
            }
            case "quarterly":
            {
                var offset = Math.Clamp(duty.Month, 1, 3) - 1;
                static DateTime QuarterStart(DateTime d) => new(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1);
                if (done is null) return DayIn(QuarterStart(today).AddMonths(offset), duty.Day);
                var after = done.Value.AddDays(Grace(91));
                for (var q = QuarterStart(done.Value); ; q = q.AddMonths(3))
                {
                    var due = DayIn(q.AddMonths(offset), duty.Day);
                    if (due > after) return due;
                }
            }
            case "yearly":
            {
                var month = Math.Clamp(duty.Month, 1, 12);
                if (done is null) return DayIn(new DateTime(today.Year, month, 1), duty.Day);
                var after = done.Value.AddDays(Grace(365));
                for (var year = done.Value.Year; ; year++)
                {
                    var due = DayIn(new DateTime(year, month, 1), duty.Day);
                    if (due > after) return due;
                }
            }
            default: // monthly
            {
                if (done is null) return DayIn(new DateTime(today.Year, today.Month, 1), duty.Day);
                var after = done.Value.AddDays(Grace(30));
                for (var month = new DateTime(done.Value.Year, done.Value.Month, 1); ; month = month.AddMonths(1))
                {
                    var due = DayIn(month, duty.Day);
                    if (due > after) return due;
                }
            }
        }
    }

    private static DateTime DayIn(DateTime month, int day) =>
        new(month.Year, month.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(month.Year, month.Month)));

    public static string Describe(Duty duty) => duty.Frequency.ToLowerInvariant() switch
    {
        "days" => $"every {duty.EveryDays} days",
        "weekly" => $"weekly on {(DayOfWeek)(Math.Clamp(duty.Day, 1, 7) % 7)}",
        "quarterly" => $"quarterly, day {duty.Day} of month {Math.Clamp(duty.Month, 1, 3)} of the quarter",
        "yearly" => $"yearly on {DayIn(new DateTime(2025, Math.Clamp(duty.Month, 1, 12), 1), duty.Day):d MMM}",
        _ => duty.Day >= 28 ? "monthly, at the end of the month" : $"monthly on the {Ordinal(duty.Day)}"
    };

    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

    /// <summary>A small starter list; everything is editable and can be removed in the Planner.</summary>
    public static List<Duty> Defaults() =>
    [
        new() { Name = "Weekly review: priorities for next week", Frequency = "weekly", Day = 5, LeadDays = 0 },
        new() { Name = "Monthly review: goals and progress", Frequency = "monthly", Day = 1, LeadDays = 2 },
        new() { Name = "Quarterly look-back on what you delivered", Frequency = "quarterly", Day = 1, Month = 1, LeadDays = 7 },
    ];
}
