using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Core.Guardian.Outcomes;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed class GuardianOpenLoopService(
    IGuardianOpenLoopRepository repository,
    WorkdayJournalService? journal = null)
{
    public async Task<GuardianOpenLoop> CaptureAsync(GuardianOpenLoop openLoop)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openLoop.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(openLoop.Source);
        if (openLoop.SourceEventId == Guid.Empty)
            throw new ArgumentException("An open loop must retain its source event ID.", nameof(openLoop));
        openLoop.CreatedAt = openLoop.CreatedAt.ToUniversalTime();
        openLoop.UpdatedAt = DateTimeOffset.UtcNow;
        openLoop.DueAt = openLoop.DueAt?.ToUniversalTime();
        var captured = await repository.UpsertAsync(openLoop);
        if (journal is not null)
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = captured.SourceEventId, CorrelationId = captured.SourceEventId,
                OccurredAt = captured.CreatedAt, EventType = "OpenLoopCreated", Category = "OpenLoop",
                Title = captured.Title, Narrative = $"Guardian opened {captured.Type}: {captured.Detail}",
                Significance = 55, TaskId = captured.TaskId, ProjectId = captured.ProjectId,
                ExternalId = captured.Id.ToString(), Outcome = "Open"
            });
        return captured;
    }

    public Task<List<GuardianOpenLoop>> GetWhatAmIWaitingOnAsync(int count = 200) => repository.GetAwaitingAsync(count);

    public async Task<IReadOnlyList<GuardianOpenLoop>> ResolveFromEvidenceAsync(GuardianLoopEvidence evidence)
    {
        if (evidence.EventId == Guid.Empty)
            throw new ArgumentException("Resolution evidence must have an event ID.", nameof(evidence));
        evidence.OccurredAt = evidence.OccurredAt.ToUniversalTime();
        var resolved = new List<GuardianOpenLoop>();
        foreach (var result in await EvaluateEvidenceAsync(evidence))
            if (result.Disposition == OpenLoopEvidenceDisposition.Resolved &&
                await repository.ResolveAsync(result.OpenLoop.Id, evidence))
            {
                resolved.Add(result.OpenLoop);
                if (journal is not null)
                    await journal.RecordIfAbsentAsync(new WorkdayEvent
                    {
                        EventId = evidence.EventId, CorrelationId = result.OpenLoop.SourceEventId,
                        ParentEventId = result.OpenLoop.SourceEventId, CausedByEventId = evidence.EventId,
                        OccurredAt = evidence.OccurredAt, EventType = "OpenLoopResolved", Category = "OpenLoop",
                        Title = result.OpenLoop.Title, Narrative = evidence.Summary, Significance = 55,
                        TaskId = result.OpenLoop.TaskId, ProjectId = result.OpenLoop.ProjectId,
                        ExternalId = result.OpenLoop.Id.ToString(), Outcome = "Resolved"
                    });
            }
        return resolved;
    }

    public async Task<IReadOnlyList<OpenLoopEvidenceResult>> EvaluateEvidenceAsync(GuardianLoopEvidence evidence)
    {
        var results = new List<OpenLoopEvidenceResult>();
        foreach (var openLoop in await repository.GetOpenAsync())
        {
            var disposition = Classify(openLoop, evidence);
            if (disposition == OpenLoopEvidenceDisposition.NoMatch) continue;
            var reason = disposition == OpenLoopEvidenceDisposition.Resolved
                ? "Evidence is explicitly linked to this open loop."
                : "Evidence shares work context but is not specific enough to close the loop automatically.";
            results.Add(new(openLoop, disposition, reason));
        }
        return results;
    }

    public static OpenLoopEvidenceDisposition Classify(GuardianOpenLoop openLoop, GuardianLoopEvidence evidence)
    {
        if (evidence.OpenLoopId == openLoop.Id) return OpenLoopEvidenceDisposition.Resolved;
        if (evidence.OccurredAt <= openLoop.CreatedAt) return OpenLoopEvidenceDisposition.NoMatch;
        if (!string.IsNullOrWhiteSpace(openLoop.ExpectedEvidenceKind) &&
            !openLoop.ExpectedEvidenceKind.Equals(evidence.Kind, StringComparison.OrdinalIgnoreCase))
            return OpenLoopEvidenceDisposition.NoMatch;
        if (Same(openLoop.EmailId, evidence.EmailId) || Same(openLoop.TeamsMessageId, evidence.TeamsMessageId) ||
            Same(openLoop.CalendarEventId, evidence.CalendarEventId) ||
            Same(openLoop.SourceExternalId, evidence.SourceExternalId))
            return OpenLoopEvidenceDisposition.Resolved;
        if (openLoop.TaskId is not null && openLoop.TaskId == evidence.TaskId)
            return OpenLoopEvidenceDisposition.Resolved;
        if ((openLoop.ProjectId is not null && openLoop.ProjectId == evidence.ProjectId) ||
            (openLoop.PersonId is not null && openLoop.PersonId == evidence.PersonId))
            return OpenLoopEvidenceDisposition.NeedsReview;
        return OpenLoopEvidenceDisposition.NoMatch;
    }

    private static bool Same(string left, string right) =>
        !string.IsNullOrWhiteSpace(left) && left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
