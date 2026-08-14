namespace Nekomata.Core.Analytics.Models;

public class DailyCapacity
{
    public int AvailableMinutes { get; set; }

    public int PlannedMinutes { get; set; }

    public int RemainingMinutes { get; set; }

    public int OverCapacityMinutes { get; set; }

    public double UtilisationPercent { get; set; }

    public int MissionMinutes { get; set; }

    public int DueTodayMinutes { get; set; }

    public int OverdueMinutes { get; set; }

    public int ScheduledMinutes { get; set; }

    public bool IsOverCapacity =>
        PlannedMinutes > AvailableMinutes;
    public List<CapacityWorkItem> PlannedWork { get; } = [];

}
public class CapacityWorkItem
{
    public string Title { get; set; } = "";

    public string Category { get; set; } = "";

    public int Minutes { get; set; }

    public double Score { get; set; }
}