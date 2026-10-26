namespace Nekomata.Models.Guardian;

public sealed class GuardianRecoveryPlan
{
    public int MinutesLost { get; set; }
    public string ProtectedObjective { get; set; } = "";
    public int ProtectedObjectiveRemainingMinutes { get; set; }
    public int ProtectedWindowOverrunMinutes { get; set; }
    public long DeferredTaskId { get; set; }
    public string DeferredTaskTitle { get; set; } = "";
    public int MinutesRecovered { get; set; }
    public DateTime DeferredUntil { get; set; }
    public DateTime ExpectedFinishAt { get; set; }
    public List<string> PreservedCommitments { get; set; } = [];
    public string ImpactSummary { get; set; } = "";
    public string RecoverySummary { get; set; } = "";
    public bool ForecastDriven { get; set; }
    public int ForecastConfidence { get; set; }
    public bool CanApply => DeferredTaskId > 0;
    public string ExpectedFinishSummary => ForecastDriven
        ? $"Projected finish after this change: {ExpectedFinishAt:HH:mm}"
        : $"Expected finish returns to {ExpectedFinishAt:HH:mm}";
}
