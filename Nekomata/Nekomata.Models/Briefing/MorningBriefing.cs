using Nekomata.Models.Guardian;

namespace Nekomata.Models.Briefing;

public class MorningBriefing
{
    // General
    public string Greeting { get; set; } = "";
    public string Headline { get; set; } = "";
    public string TodaySummary { get; set; } = "";
    public string YesterdaySummary { get; set; } = "";
    public string GuardianComment { get; set; } = "";
    public string CapacitySummary { get; set; } = "";
    public string AiSummary { get; set; } = "";
    public string CalendarSummary { get; set; } = "";
    public string MeetingSummary { get; set; } = "";
    public string AwarenessSummary { get; set; } = "";

    // Yesterday
    public int MissionsCompletedYesterday { get; set; }
    public int MissionsCancelledYesterday { get; set; }
    public int FocusMinutesYesterday { get; set; }
    public decimal BusinessValueYesterday { get; set; }
    public double AverageScoreYesterday { get; set; }

    // Today
    public int MissionsCompletedToday { get; set; }
    public int FocusMinutesToday { get; set; }
    public string FocusTimeTodayFormatted => FormatMinutes(FocusMinutesToday);
    public int TasksDueToday { get; set; }
    public int OverdueTasks { get; set; }
    public int CriticalTasks { get; set; }
    public int UnscheduledTasks { get; set; }

    public int PlannedMinutesToday { get; set; }
    public decimal BusinessValueToday { get; set; }

    public string FocusTimeYesterdayFormatted =>
    FormatMinutes(FocusMinutesYesterday);

    public string PlannedTimeTodayFormatted =>
        FormatMinutes(PlannedMinutesToday);

    private static string FormatMinutes(int minutes)
    {
        if (minutes <= 0)
            return "0m";

        var ts = TimeSpan.FromMinutes(minutes);

        if (ts.TotalHours < 1)
            return $"{ts.Minutes}m";

        return $"{(int)ts.TotalHours}h {ts.Minutes}m";
    }

    // Capacity
    public int AvailableMinutesToday { get; set; }
    public int RemainingCapacityMinutes { get; set; }
    public double CapacityUsedPercent { get; set; }
    public bool IsOverCapacity { get; set; }
    public int OvertimeMinutesWorked { get; set; }
    public int ExpectedOvertimeMinutes { get; set; }
    public string BurnoutRisk { get; set; } = "Low";
    public DateTime ExpectedFinishAt { get; set; }

    // Objective
    public string PrimaryFocus { get; set; } = "";
    public string ObjectiveTitle { get; set; } = "";
    public string ObjectiveReason { get; set; } = "";

    public int ObjectiveScore { get; set; }
    public decimal ObjectiveBusinessValue { get; set; }
    public int ObjectiveEstimatedMinutes { get; set; }
    public string ObjectiveEstimatedTimeFormatted =>
    FormatMinutes(ObjectiveEstimatedMinutes);

    public DateTime? ObjectiveStartBefore { get; set; }

    public long? ObjectiveTaskId { get; set; }
    public long? ObjectiveProjectId { get; set; }

    // AI

    public int GuardianConfidence { get; set; }

    public List<GuardianReason> GuardianReasons { get; } = [];

    public List<DecisionRisk> GuardianRisks { get; } = [];

    public List<GuardianOpportunity> Opportunities { get; } = [];

    public int WorkspaceHealthScore { get; set; }

    public List<string> HealthWarnings { get; } = [];

    public string HealthSummary { get; set; } =
    "Workspace health based on connected systems.";
}