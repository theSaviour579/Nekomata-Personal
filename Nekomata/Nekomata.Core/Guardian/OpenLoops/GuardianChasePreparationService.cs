using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed class GuardianChasePreparationService(
    GuardianOpenLoopAgeService ageService,
    IGuardianChaseRepository repository,
    WorkdayJournalService journal)
{
    public async Task<IReadOnlyList<GuardianChaseAttempt>> PrepareMatureAsync(
        NekomataWorkspace workspace,
        DateTimeOffset now)
    {
        var prepared = new List<GuardianChaseAttempt>();
        foreach (var chase in (await ageService.EvaluateAsync(now))
                     .Where(x => x.ChaseMature || x.ChaseState == GuardianChaseState.Prepared))
        {
            var existing = await repository.GetPreparedAsync(chase.OpenLoop.Id);
            if (existing is not null) { prepared.Add(existing); continue; }

            var context = ResolveContext(chase.OpenLoop, workspace);
            var attempt = new GuardianChaseAttempt
            {
                OpenLoopId = chase.OpenLoop.Id,
                AttemptNumber = await repository.GetAttemptCountAsync(chase.OpenLoop.Id) + 1,
                PreparedAt = now.ToUniversalTime(), Recipient = chase.OpenLoop.WaitingOn,
                RecipientAddress = chase.OpenLoop.WaitingOnAddress,
                Subject = $"Follow-up: {CleanTitle(chase.OpenLoop.Title)}",
                Body = BuildDraft(chase.OpenLoop.WaitingOn, context), ContextLabel = context
            };
            await repository.AddAsync(attempt);
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = attempt.ProvenanceEventId, CorrelationId = chase.OpenLoop.SourceEventId,
                ParentEventId = chase.OpenLoop.SourceEventId, OccurredAt = now,
                EventType = "OpenLoopChasePrepared", Category = "OpenLoop", Significance = 60,
                Title = attempt.Subject, Narrative = $"Prepared chase {attempt.AttemptNumber} for {attempt.Recipient}; approval required.",
                TaskId = chase.OpenLoop.TaskId, ProjectId = chase.OpenLoop.ProjectId,
                ExternalId = attempt.Id.ToString(), Outcome = "Prepared"
            });
            prepared.Add(attempt);
        }
        return prepared;
    }

    private static string ResolveContext(GuardianOpenLoop loop, NekomataWorkspace workspace)
    {
        if (loop.ProjectId is long projectId)
        {
            var project = workspace.Projects.FirstOrDefault(x => x.Id == projectId);
            if (project is not null) return project.Name;
        }
        return CleanTitle(loop.Title);
    }

    private static string CleanTitle(string title) => title.StartsWith("Awaiting reply: ", StringComparison.OrdinalIgnoreCase)
        ? title[16..].Trim() : title.Trim();

    private static string BuildDraft(string recipient, string context)
    {
        var firstName = recipient.Split([' ', '/', ',', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi," : $"Hi {firstName},";
        return $"{greeting}\n\nJust following up on my message about {context}. Could you let me know when you get a chance?\n\nThanks,";
    }
}
