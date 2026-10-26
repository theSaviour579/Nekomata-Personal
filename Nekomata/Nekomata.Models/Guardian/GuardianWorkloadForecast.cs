namespace Nekomata.Models.Guardian;

public sealed class GuardianWorkloadItem
{
    public long? TaskId { get; set; }
    public string Title { get; set; } = "";
    public int Minutes { get; set; }
    public int Score { get; set; }
    public bool IsCritical { get; set; }
    public DateTime? DueAt { get; set; }
}

public sealed class GuardianWorkloadForecastInput
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;
    public DateTime WorkdayEnd { get; set; }
    public int RemainingWorkableMinutes { get; set; }
    public int FixedCommitmentMinutes { get; set; }
    public int PlannedWorkMinutes { get; set; }
    public int InterruptionAllowanceMinutes { get; set; }
    public int NonWorkingMinutesRemaining { get; set; }
    public int InterruptionEvidenceCount { get; set; }
    public bool CalendarLoaded { get; set; }
    public IReadOnlyList<GuardianWorkloadItem> WorkItems { get; set; } = [];
}

public sealed class GuardianWorkloadForecast
{
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset ProjectedFinishAt { get; set; }
    public int RemainingWorkableMinutes { get; set; }
    public int PlannedWorkMinutes { get; set; }
    public int FixedCommitmentMinutes { get; set; }
    public int InterruptionAllowanceMinutes { get; set; }
    public int MinutesAtRisk { get; set; }
    public int ConfidencePercent { get; set; }
    public string StatusLabel { get; set; } = "AWAITING EVIDENCE";
    public string Summary { get; set; } = "Workload forecast is waiting for calendar and capacity evidence.";
    public string Evidence { get; set; } = "";
    public List<string> ThreatenedCommitments { get; set; } = [];
    public bool NeedsRecovery => MinutesAtRisk > 0;
    public string ProjectedFinishLabel => ProjectedFinishAt == default ? "Finish unavailable" : $"Projected finish {ProjectedFinishAt.LocalDateTime:HH:mm}";
    public string InterruptionAllowanceLabel => InterruptionAllowanceMinutes == 0 ? "No learned interruption allowance" : $"Includes {InterruptionAllowanceMinutes} min interruption allowance";
    public string StripText => ProjectedFinishAt == default
        ? "Forecast awaiting evidence"
        : $"Forecast: {StatusLabel.ToLowerInvariant()} · finish {ProjectedFinishAt.LocalDateTime:HH:mm} · {MinutesAtRisk}m at risk";
}
