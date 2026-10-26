namespace Nekomata.Models.Guardian;

public sealed class GuardianWorkThreadImpact
{
    public bool IsBlockingCurrentObjective { get; set; }
    public bool IsBlockingActiveTask { get; set; }
    public bool HasUpcomingCommitment { get; set; }
    public bool HasMatureOpenLoop { get; set; }
    public bool HasUnboundRequiredWork { get; set; }
    public bool IsStale { get; set; }
    public string ProjectPriority { get; set; } = "";
    public decimal BusinessValue { get; set; }
}

public sealed class GuardianWorkThreadSummary
{
    public Guid ThreadId { get; set; }
    public string Title { get; set; } = "";
    public string PrimaryProject { get; set; } = "";
    public int ActiveTasks { get; set; }
    public int Meetings { get; set; }
    public int OpenLoops { get; set; }
    public int BlockingDependencies { get; set; }
    public int RecentCommunications { get; set; }
    public int PlannedEffortMinutes { get; set; }
    public int RecordedEffortMinutes { get; set; }
    public int RemainingEffortMinutes { get; set; }
    public int PlannedMeetingMinutes { get; set; }
    public int RecordedMeetingMinutes { get; set; }
    public int AttributedMeetingMinutes { get; set; }
    public DateTimeOffset? LastMeaningfulActivity { get; set; }
    public string NextCommitment { get; set; } = "";
    public string HighestImpactDependency { get; set; } = "";
    public List<string> RecentChanges { get; set; } = [];
    public List<string> TeamsConversations { get; set; } = [];
    public List<string> CompletedInterventions { get; set; } = [];
    public List<string> KeyEvidence { get; set; } = [];
    public GuardianWorkThreadImpact Impact { get; set; } = new();

    public string NextCommitmentLabel => string.IsNullOrWhiteSpace(NextCommitment) ? "None identified" : NextCommitment;
    public string RemainingEffortLabel => FormatDuration(RemainingEffortMinutes);
    public string PlannedMeetingEffortLabel => FormatDuration(PlannedMeetingMinutes);
    public string RecordedMeetingEffortLabel => FormatDuration(RecordedMeetingMinutes);

    private static string FormatDuration(int minutes)
    {
        if (minutes <= 0) return "0 min";
        var hours = minutes / 60;
        var remainder = minutes % 60;
        if (hours == 0) return $"{remainder} min";
        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
    }
}
