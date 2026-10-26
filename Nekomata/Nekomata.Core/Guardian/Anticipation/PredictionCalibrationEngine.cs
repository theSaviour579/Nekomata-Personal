using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed class PredictionCalibrationEngine
{
    public PredictionCalibrationSummary Evaluate(IEnumerable<ScheduledIntervention> interventions, DateTimeOffset now)
    {
        var outcomes = interventions.Select(x => Outcome(x, now)).ToList();
        var evaluated = outcomes.Where(x => x.Disposition != PredictionOutcomeDisposition.Pending).ToList();
        var accepted = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Accepted);
        var overridden = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Overridden);
        var premature = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Premature);
        var late = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Late);
        var missed = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Missed);
        var decisive = accepted + overridden + late + missed;
        var timed = accepted + late + missed;
        return new PredictionCalibrationSummary
        {
            TotalPredictions = outcomes.Count, EvaluatedPredictions = evaluated.Count,
            PendingPredictions = outcomes.Count - evaluated.Count, AcceptedPredictions = accepted,
            OverriddenPredictions = overridden,
            SupersededPredictions = evaluated.Count(x => x.Disposition == PredictionOutcomeDisposition.Superseded),
            PrematurePredictions = premature, LatePredictions = late, MissedPredictions = missed,
            EstimatedAdminMinutesSaved = outcomes.Sum(x => x.EstimatedAdminMinutesSaved),
            AcceptancePercent = Percent(accepted, decisive),
            FalsePositivePercent = Percent(premature + overridden, evaluated.Count),
            TimingAccuracyPercent = Percent(accepted, timed), Outcomes = outcomes
        };
    }

    private static PredictionOutcome Outcome(ScheduledIntervention value, DateTimeOffset now)
    {
        var observed = value.CompletedAt;
        var disposition = value.Status switch
        {
            ScheduledInterventionStatus.Completed when observed > value.ExpiresAt => PredictionOutcomeDisposition.Late,
            ScheduledInterventionStatus.Completed => PredictionOutcomeDisposition.Accepted,
            ScheduledInterventionStatus.Cancelled => PredictionOutcomeDisposition.Overridden,
            ScheduledInterventionStatus.Superseded when observed < value.TriggerAt => PredictionOutcomeDisposition.Premature,
            ScheduledInterventionStatus.Superseded when value.ExpiresAt <= (observed ?? now) => PredictionOutcomeDisposition.Missed,
            ScheduledInterventionStatus.Superseded => PredictionOutcomeDisposition.Superseded,
            _ when value.ExpiresAt <= now => PredictionOutcomeDisposition.Missed,
            _ => PredictionOutcomeDisposition.Pending
        };
        return new PredictionOutcome
        {
            InterventionId = value.Id, PredictionId = value.PredictionId, ActionType = value.ActionType,
            Title = value.Title, Disposition = disposition, TriggerAt = value.TriggerAt, ObservedAt = observed,
            Reason = Explanation(disposition),
            EstimatedAdminMinutesSaved = disposition == PredictionOutcomeDisposition.Accepted ? SavedMinutes(value.ActionType) : 0
        };
    }

    private static string Explanation(PredictionOutcomeDisposition value) => value switch
    {
        PredictionOutcomeDisposition.Accepted => "The intervention completed within its permitted window.",
        PredictionOutcomeDisposition.Overridden => "The intervention was explicitly cancelled or overridden.",
        PredictionOutcomeDisposition.Premature => "Changing evidence invalidated the prediction before its trigger time.",
        PredictionOutcomeDisposition.Late => "The intervention completed after its permitted window.",
        PredictionOutcomeDisposition.Missed => "The permitted window expired before completion.",
        PredictionOutcomeDisposition.Superseded => "Changing evidence replaced the prediction after it became actionable.",
        _ => "The intervention has not yet reached a measurable outcome."
    };

    private static int SavedMinutes(string actionType) => actionType switch
    {
        "PrepareMeetingContext" => 15, "CheckReadinessGate" => 10,
        "PrepareDependencyFollowUp" => 8, "PrepareMeetingReply" => 5,
        "WaitForDependency" => 2, _ => 3
    };
    private static int Percent(int value, int total) => total == 0 ? 0 : (int)Math.Round(value * 100d / total);
}
