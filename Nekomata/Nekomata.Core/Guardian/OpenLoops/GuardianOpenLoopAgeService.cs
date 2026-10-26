using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed class GuardianOpenLoopAgeService(
    IGuardianOpenLoopRepository repository,
    WorkdayJournalService journal,
    IGuardianChaseRepository? chaseRepository = null)
{
    public const int DefaultChaseAfterWorkingDays = 2;

    public async Task<IReadOnlyList<GuardianOpenLoopChase>> EvaluateAsync(
        DateTimeOffset now,
        int chaseAfterWorkingDays = DefaultChaseAfterWorkingDays)
    {
        var results = new List<GuardianOpenLoopChase>();
        foreach (var loop in await repository.GetAwaitingAsync())
        {
            var age = WorkingDaysBetween(loop.CreatedAt, now);
            var initiallyMature = age >= chaseAfterWorkingDays;
            var attempts = chaseRepository is null ? [] : await chaseRepository.GetAttemptsAsync(loop.Id);
            var provisional = new GuardianOpenLoopChase(loop, age, initiallyMature, "");
            var state = GuardianChasePolicy.DeriveState(provisional, attempts, now);
            var mature = state is GuardianChaseState.Due or GuardianChaseState.EscalationDue;
            var subject = string.IsNullOrWhiteSpace(loop.WaitingOn) ? loop.Title : loop.WaitingOn;
            var latest = attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
            var message = state switch
            {
                GuardianChaseState.CoolingDown => $"You're waiting on {subject}. I chased {(string.IsNullOrWhiteSpace(loop.WaitingOn) ? "them" : loop.WaitingOn)} recently, so no further action is due yet.",
                GuardianChaseState.Prepared => $"A follow-up to {subject} is prepared and awaiting approval.",
                GuardianChaseState.Approved => string.IsNullOrWhiteSpace(latest?.LastSendError)
                    ? $"The follow-up to {subject} is approved but has not been sent."
                    : $"The follow-up to {subject} is approved but could not be sent: {latest.LastSendError}",
                GuardianChaseState.EscalationDue => $"You've been waiting on {subject} for {age} working days; escalation is due after {attempts.Count} chases.",
                _ when mature => $"You've been waiting on {subject} for {age} working day{(age == 1 ? "" : "s")}.",
                _ => $"Waiting on {subject} for {age} working day{(age == 1 ? "" : "s")}."
            };
            var chase = new GuardianOpenLoopChase(loop, age, mature, message, state, latest?.NextChaseDueAt, attempts.Count);
            results.Add(chase);
            if (initiallyMature)
                await journal.RecordIfAbsentAsync(ToMaturityEvent(chase, now));
        }
        return results.OrderByDescending(x => x.ChaseMature).ThenByDescending(x => x.WorkingDaysOpen).ToList();
    }

    public static int WorkingDaysBetween(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var start = createdAt.LocalDateTime.Date;
        var end = now.LocalDateTime.Date;
        if (end <= start) return 0;
        var count = 0;
        for (var date = start.AddDays(1); date <= end; date = date.AddDays(1))
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) count++;
        return count;
    }

    private static WorkdayEvent ToMaturityEvent(GuardianOpenLoopChase chase, DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(), CorrelationId = chase.OpenLoop.SourceEventId,
        ParentEventId = chase.OpenLoop.SourceEventId, OccurredAt = now,
        EventType = "OpenLoopChaseMatured", Category = "OpenLoop", Significance = 65,
        Title = chase.OpenLoop.Title, Narrative = chase.Message,
        TaskId = chase.OpenLoop.TaskId, ProjectId = chase.OpenLoop.ProjectId,
        ExternalId = chase.OpenLoop.Id.ToString(), Outcome = "ChaseRequired",
        Metadata = $"{{\"workingDaysOpen\":{chase.WorkingDaysOpen}}}"
    };
}
