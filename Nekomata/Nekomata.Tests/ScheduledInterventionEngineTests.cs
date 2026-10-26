using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class ScheduledInterventionEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T08:00:00+01:00");

    [Fact]
    public void Future_prediction_becomes_scheduled_intervention()
    {
        var prediction = Prediction(AnticipatedActionStatus.Waiting, Now.AddHours(2));

        var result = Assert.Single(new ScheduledInterventionEngine().Reconcile([prediction], [], Now));

        Assert.Equal(ScheduledInterventionStatus.Scheduled, result.Status);
        Assert.Equal(prediction.Id, result.PredictionId);
        Assert.Equal(Now.AddHours(2), result.TriggerAt);
    }

    [Fact]
    public void Mature_prediction_becomes_due()
    {
        var prediction = Prediction(AnticipatedActionStatus.ReadyToExecute, Now.AddMinutes(-1));

        var result = Assert.Single(new ScheduledInterventionEngine().Reconcile([prediction], [], Now));

        Assert.Equal(ScheduledInterventionStatus.Due, result.Status);
    }

    [Fact]
    public void Prepared_prediction_preserves_prepared_state_and_content()
    {
        var prediction = Prediction(AnticipatedActionStatus.Prepared, Now.AddHours(1));
        prediction.PreparedContent = "Prepared meeting pack";

        var result = Assert.Single(new ScheduledInterventionEngine().Reconcile([prediction], [], Now));

        Assert.Equal(ScheduledInterventionStatus.Prepared, result.Status);
        Assert.Equal("Prepared meeting pack", result.PreparedContent);
    }

    [Fact]
    public void Missing_prediction_supersedes_previous_intervention()
    {
        var prediction = Prediction(AnticipatedActionStatus.Waiting, Now.AddHours(2));
        var prior = Assert.Single(new ScheduledInterventionEngine().Reconcile([prediction], [], Now));

        var result = Assert.Single(new ScheduledInterventionEngine().Reconcile([], [prior], Now.AddMinutes(5)));

        Assert.Equal(ScheduledInterventionStatus.Superseded, result.Status);
        Assert.Contains("no longer valid", result.Outcome);
    }

    private static AnticipatedAction Prediction(AnticipatedActionStatus status, DateTimeOffset triggerAt) => new()
    {
        Id = Guid.NewGuid(), WorkThreadId = Guid.NewGuid(), ProposedAction = "WaitForDependency",
        Reason = "The dependency is cooling down.", Evidence = ["A chase was sent yesterday"],
        Authority = GuardianActionAuthority.Observe, Status = status, CreatedAt = Now,
        ExecuteAfter = triggerAt, ExpiresAt = Now.AddDays(2)
    };
}
