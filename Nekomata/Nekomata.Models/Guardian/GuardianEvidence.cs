namespace Nekomata.Models.Guardian;

public class GuardianEvidence
{
    // Mission

    public bool HasMission { get; set; }

    public int MissionScore { get; set; }

    public decimal MissionBusinessValue { get; set; }

    public TimeSpan MissionDuration { get; set; }

    // Tasks

    public int OpenTasks { get; set; }

    public int DueToday { get; set; }

    public int Overdue { get; set; }

    public int Undated { get; set; }

    public int Critical { get; set; }

    // Capacity

    public bool OverCapacity { get; set; }

    public int CapacityMinutesRemaining { get; set; }

    // Projects

    public int ActiveProjects { get; set; }

    // History

    public int MissionsCompletedYesterday { get; set; }

    // Future integrations

    public bool HaloConnected { get; set; }

    public bool CalendarConnected { get; set; }

    public bool SqlConnected { get; set; }

    public bool EmailConnected { get; set; }

    //-------------------------------------------------
    // Workspace Health
    //-------------------------------------------------

    public int WorkspaceHealthScore { get; set; }

    public List<string> HealthWarnings { get; } = [];

    public string CapacitySummary { get; set; } = "";
}