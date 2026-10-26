namespace Nekomata.Models.Guardian;

public enum PredictionOutcomeDisposition
{
    Pending, Accepted, Overridden, Superseded, Premature, Late, Missed
}

public sealed class PredictionOutcome
{
    public Guid InterventionId { get; set; }
    public Guid PredictionId { get; set; }
    public string ActionType { get; set; } = "";
    public string Title { get; set; } = "";
    public PredictionOutcomeDisposition Disposition { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset TriggerAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public int EstimatedAdminMinutesSaved { get; set; }
}

public sealed class PredictionCalibrationSummary
{
    public int TotalPredictions { get; set; }
    public int EvaluatedPredictions { get; set; }
    public int PendingPredictions { get; set; }
    public int AcceptedPredictions { get; set; }
    public int OverriddenPredictions { get; set; }
    public int SupersededPredictions { get; set; }
    public int PrematurePredictions { get; set; }
    public int LatePredictions { get; set; }
    public int MissedPredictions { get; set; }
    public int EstimatedAdminMinutesSaved { get; set; }
    public int AcceptancePercent { get; set; }
    public int FalsePositivePercent { get; set; }
    public int TimingAccuracyPercent { get; set; }
    public List<PredictionOutcome> Outcomes { get; set; } = [];
}
