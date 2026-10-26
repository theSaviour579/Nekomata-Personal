namespace Nekomata.Models.Guardian;

public sealed class GuardianSituationInput
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;
    public string CurrentObjective { get; set; } = "No active objective";
    public string? NowSummary { get; set; }
    public string? NextSummary { get; set; }
    public string? LaterSummary { get; set; }
    public bool MissionActive { get; set; }
    public int JudgementCount { get; set; }
    public int PriorityEmailCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public int AtRiskProjectCount { get; set; }
    public int QueuedInterruptionCount { get; set; }
    public int BriefingSignalCount { get; set; }
    public int OverCapacityMinutes { get; set; }
    public bool CalendarLoaded { get; set; }
    public string NextDisruption { get; set; } = "None scheduled";
    public DateTimeOffset? NextDisruptionAt { get; set; }
    public string SystemHealth { get; set; } = "Healthy";
    public bool SystemNeedsAttention { get; set; }
}

public sealed class GuardianSituationSignal
{
    public string Source { get; set; } = "Guardian";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public int Significance { get; set; }
}

public sealed class GuardianSituationSnapshot
{
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.Now;
    public string CurrentObjective { get; set; } = "No active objective";
    public string NowSummary { get; set; } = "Free window";
    public string NextSummary { get; set; } = "No next commitment";
    public string LaterSummary { get; set; } = "No material pressure identified";
    public int JudgementCount { get; set; }
    public int DayConfidence { get; set; } = 100;
    public string NextDisruption { get; set; } = "None scheduled";
    public DateTimeOffset? NextDisruptionAt { get; set; }
    public string SystemHealth { get; set; } = "Healthy";
    public List<GuardianSituationSignal> Signals { get; set; } = [];
    public string StripText => $"NOW · {NowSummary}   |   NEXT · {NextSummary}   |   LATER · {LaterSummary}   |   DAY · {DayConfidence}% confidence";
}
