using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed record GuardianChaseSendResult(GuardianChaseAttempt Attempt, bool Sent, string Message);

public sealed class GuardianChaseWorkflowService(
    IGuardianChaseRepository repository,
    IEmailService email,
    WorkdayJournalService journal)
{
    public async Task<IReadOnlyList<GuardianChaseAttempt>> ApprovePreparedAsync(DateTimeOffset now)
    {
        var approved = new List<GuardianChaseAttempt>();
        foreach (var attempt in await repository.GetByStatusAsync(GuardianChaseStatus.Prepared))
        {
            if (!await repository.ApproveAsync(attempt.Id, now)) continue;
            attempt.Status = GuardianChaseStatus.Approved;
            attempt.ApprovedAt = now.ToUniversalTime();
            approved.Add(attempt);
            await journal.RecordIfAbsentAsync(Event(attempt, "OpenLoopChaseApproved", "Approved", now,
                $"Chase {attempt.AttemptNumber} for {attempt.Recipient} was approved."));
        }
        return approved;
    }

    public async Task<IReadOnlyList<GuardianChaseSendResult>> SendApprovedAsync(DateTimeOffset now)
    {
        var results = new List<GuardianChaseSendResult>();
        foreach (var attempt in await repository.GetByStatusAsync(GuardianChaseStatus.Approved))
        {
            if (attempt.LastSendError.StartsWith("DELIVERY_CONFIRMED_STATE_PENDING", StringComparison.Ordinal))
            {
                results.Add(new(attempt, true, "Graph confirmed submission previously, but Guardian still needs to reconcile local state; it was not sent again."));
                continue;
            }
            if (string.IsNullOrWhiteSpace(attempt.RecipientAddress) || attempt.RecipientAddress.Contains(';'))
            {
                var error = string.IsNullOrWhiteSpace(attempt.RecipientAddress)
                    ? "No recipient email address is linked to this chase."
                    : "Multiple recipients require individual approval before sending.";
                await RecordFailureAsync(attempt, now, error);
                results.Add(new(attempt, false, error));
                continue;
            }
            try
            {
                await email.SendMessageAsync(attempt.RecipientAddress, attempt.Subject, attempt.Body);
                var due = GuardianChasePolicy.NextChaseDue(now, attempt.AttemptNumber);
                if (!await repository.MarkSentAsync(attempt.Id, now, due))
                    throw new InvalidOperationException("DELIVERY_CONFIRMED_STATE_PENDING: Graph confirmed submission, but Guardian could not record the Sent transition. Do not retry automatically.");
                attempt.Status = GuardianChaseStatus.Sent;
                attempt.SentAt = now.ToUniversalTime();
                attempt.NextChaseDueAt = due.ToUniversalTime();
                attempt.SendAttemptCount++;
                await journal.RecordIfAbsentAsync(Event(attempt, "OpenLoopChaseSent", "Sent", now,
                    $"Chase {attempt.AttemptNumber} was sent to {attempt.Recipient}."));
                await journal.RecordIfAbsentAsync(Event(attempt, "OpenLoopChaseCooldownStarted", "CoolingDown", now,
                    $"No further chase is due until {due.LocalDateTime:dddd dd MMMM}."));
                results.Add(new(attempt, true, $"Sent to {attempt.Recipient}; next review is {due.LocalDateTime:dddd dd MMMM}."));
            }
            catch (Exception ex)
            {
                await RecordFailureAsync(attempt, now, ex.Message);
                results.Add(new(attempt, false, $"Approved but could not be sent: {ex.Message}"));
            }
        }
        return results;
    }

    private async Task RecordFailureAsync(GuardianChaseAttempt attempt, DateTimeOffset now, string error)
    {
        await repository.RecordSendFailureAsync(attempt.Id, now, error);
        attempt.LastSendAttemptAt = now.ToUniversalTime();
        attempt.LastSendError = error;
        attempt.SendAttemptCount++;
        await journal.RecordAsync(Event(attempt, "OpenLoopChaseSendFailed", "Approved", now,
            $"Approved chase {attempt.AttemptNumber} could not be sent: {error}"));
    }

    private static WorkdayEvent Event(GuardianChaseAttempt attempt, string type, string outcome, DateTimeOffset now, string narrative) => new()
    {
        EventId = Guid.NewGuid(), CorrelationId = attempt.OpenLoopId, ParentEventId = attempt.ProvenanceEventId,
        OccurredAt = now, EventType = type, Category = "OpenLoop", Title = attempt.Subject,
        Narrative = narrative, ExternalId = attempt.Id.ToString(), Outcome = outcome, Significance = 65
    };
}
