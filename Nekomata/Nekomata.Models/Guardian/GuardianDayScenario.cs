namespace Nekomata.Models.Guardian;

public sealed class GuardianScenarioWorkItem
{
    public long TaskId { get; set; }
    public long? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public int Minutes { get; set; }
    public int Score { get; set; }
    public int Rank { get; set; }
    public bool IsCritical { get; set; }
    public DateTime? DueAt { get; set; }
}

public sealed class GuardianScenarioCommitment
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }
}

public sealed class GuardianDayScenarioInput
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;
    public TimeSpan WorkdayStart { get; set; } = new(8, 0, 0);
    public TimeSpan WorkdayEnd { get; set; } = new(16, 30, 0);
    public TimeSpan LunchStart { get; set; } = new(12, 0, 0);
    public int LunchMinutes { get; set; } = 60;
    public bool IncludeLunch { get; set; } = true;
    public int MinimumBlockMinutes { get; set; } = 30;
    public int HorizonWorkingDays { get; set; } = 3;
    public bool CalendarLoaded { get; set; }
    public long? PreferredTaskId { get; set; }
    public TimeSpan? LeaveTodayAt { get; set; }
    public IReadOnlyList<GuardianScenarioWorkItem> WorkItems { get; set; } = [];
    public IReadOnlyList<GuardianScenarioCommitment> Commitments { get; set; } = [];
}

public sealed class GuardianScenarioBlock
{
    public long TaskId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public int Minutes => Math.Max(0, (int)(End - Start).TotalMinutes);
    public string DayLabel => Start.ToString("ddd dd MMM");
    public string TimeLabel => $"{Start:HH:mm}–{End:HH:mm}";
}

public sealed class GuardianDayScenario
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public DateTimeOffset? ProjectedFinishAt { get; set; }
    public int ConfidencePercent { get; set; }
    public int ScheduledMinutes { get; set; }
    public int UnscheduledMinutes { get; set; }
    public List<GuardianScenarioBlock> Blocks { get; set; } = [];
    public List<string> DisplacedCommitments { get; set; } = [];
    public List<string> ThreatenedCommitments { get; set; } = [];
    public List<string> ProjectConsequences { get; set; } = [];
    public List<string> StakeholderActions { get; set; } = [];
    public List<string> Assumptions { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
    public string ComparisonSummary { get; set; } = "";
    public int MaterialDifferenceCount { get; set; }
    public bool IsMateriallyDifferent => MaterialDifferenceCount > 0;
    public bool CanPrepare => Blocks.Count > 0;
    public string FinishLabel => ProjectedFinishAt is DateTimeOffset finish ? $"Finish {finish:ddd HH:mm}" : "Not all work fits";
    public string RiskLabel => UnscheduledMinutes > 0 ? $"{UnscheduledMinutes} min outside the horizon" : ThreatenedCommitments.Count > 0 ? $"{ThreatenedCommitments.Count} commitment(s) threatened" : "Plan fits";
    public string DifferenceLabel => IsMateriallyDifferent ? $"{MaterialDifferenceCount} material change(s)" : "Same practical outcome";
}
