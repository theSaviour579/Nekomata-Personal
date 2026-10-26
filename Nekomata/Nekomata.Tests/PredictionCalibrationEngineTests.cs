using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class PredictionCalibrationEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T12:00:00+01:00");

    [Fact]
    public void Evaluate_classifies_outcomes_and_calculates_metrics()
    {
        var values = new[]
        {
            Intervention(ScheduledInterventionStatus.Completed, Now.AddHours(-2), Now.AddHours(-1), Now.AddMinutes(-90)),
            Intervention(ScheduledInterventionStatus.Cancelled, Now.AddHours(-2), Now.AddHours(-1), Now.AddMinutes(-90)),
            Intervention(ScheduledInterventionStatus.Superseded, Now.AddHours(1), Now.AddHours(2), Now),
            Intervention(ScheduledInterventionStatus.Superseded, Now.AddHours(-2), Now.AddHours(-1), Now.AddMinutes(-90)),
            Intervention(ScheduledInterventionStatus.Scheduled, Now.AddHours(-2), Now.AddHours(-1), null),
            Intervention(ScheduledInterventionStatus.Scheduled, Now.AddHours(1), Now.AddHours(2), null)
        };

        var result = new PredictionCalibrationEngine().Evaluate(values, Now);

        Assert.Equal(6, result.TotalPredictions);
        Assert.Equal(5, result.EvaluatedPredictions);
        Assert.Equal(1, result.PendingPredictions);
        Assert.Equal(1, result.AcceptedPredictions);
        Assert.Equal(1, result.OverriddenPredictions);
        Assert.Equal(1, result.PrematurePredictions);
        Assert.Equal(1, result.SupersededPredictions);
        Assert.Equal(1, result.MissedPredictions);
        Assert.Equal(33, result.AcceptancePercent);
        Assert.Equal(40, result.FalsePositivePercent);
        Assert.Equal(50, result.TimingAccuracyPercent);
        Assert.Equal(15, result.EstimatedAdminMinutesSaved);
    }

    [Fact]
    public void Evaluate_marks_completion_after_expiry_as_late()
    {
        var result = new PredictionCalibrationEngine().Evaluate(
            [Intervention(ScheduledInterventionStatus.Completed, Now.AddHours(-3), Now.AddHours(-2), Now.AddHours(-1))], Now);

        Assert.Equal(PredictionOutcomeDisposition.Late, Assert.Single(result.Outcomes).Disposition);
        Assert.Equal(1, result.LatePredictions);
        Assert.Equal(0, result.EstimatedAdminMinutesSaved);
    }

    private static ScheduledIntervention Intervention(ScheduledInterventionStatus status, DateTimeOffset trigger,
        DateTimeOffset expires, DateTimeOffset? completed) => new()
    {
        Id = Guid.NewGuid(), PredictionId = Guid.NewGuid(), ActionType = "PrepareMeetingContext",
        Title = "Prepare meeting context", Status = status, TriggerAt = trigger, ExpiresAt = expires,
        ScheduledAt = trigger.AddHours(-1), CompletedAt = completed
    };
}
