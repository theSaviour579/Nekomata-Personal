namespace Nekomata.Models.Planning;

public class WorkingDaySettings
{
    public TimeSpan StartTime { get; set; } =
        new(8, 0, 0);

    public TimeSpan EndTime { get; set; } =
        new(16, 30, 0);

    public TimeSpan LunchStartTime { get; set; } =
        new(12, 30, 0);

    public int LunchDurationMinutes { get; set; } =
        60;

    public bool IncludeLunchBreak { get; set; } =
        true;

    public int MinimumFocusBlockMinutes { get; set; } =
        30;

    public DateTime GetStart(DateTime date) =>
        date.Date.Add(StartTime);

    public DateTime GetEnd(DateTime date) =>
        date.Date.Add(EndTime);

    public DateTime GetLunchStart(DateTime date) =>
        date.Date.Add(LunchStartTime);

    public DateTime GetLunchEnd(DateTime date) =>
        GetLunchStart(date)
            .AddMinutes(LunchDurationMinutes);
}