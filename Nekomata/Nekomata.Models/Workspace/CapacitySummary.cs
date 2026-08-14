namespace Nekomata.Models.Workspace;

public sealed class CapacitySummary
{
    public int WorkingMinutesToday { get; set; } = 450;
    public int ScheduledMinutesToday { get; set; }
    public int ScheduledMinutesRemaining { get; set; }
    public int ScheduledFocusMinutesToday { get; set; }
    public int AvailableMinutesToday { get; set; }
    public int PlannedMinutesToday { get; set; }
    public int RemainingMinutesToday => Math.Max(0, AvailableMinutesToday - PlannedMinutesToday);
    public int OverCapacityMinutes => Math.Max(0, PlannedMinutesToday - AvailableMinutesToday);
    public bool IsOverCapacity => OverCapacityMinutes > 0;
    public int OvertimeMinutesWorked { get; set; }
    public int ExpectedOvertimeMinutes => OverCapacityMinutes;
    public DateTime WorkdayStart { get; set; }
    public DateTime WorkdayEnd { get; set; }
    public DateTime ExpectedFinishAt { get; set; }
    public string BurnoutRisk { get; set; } = "Low";
    public bool IsWorkingOvertime => OvertimeMinutesWorked > 0;
    public List<CapacityPushbackSuggestion> PushbackSuggestions { get; set; } = [];
    public double UtilisationPercent => WorkingMinutesToday == 0
        ? 0
        : Math.Min(100, (ScheduledMinutesToday + PlannedMinutesToday) * 100d / WorkingMinutesToday);
}