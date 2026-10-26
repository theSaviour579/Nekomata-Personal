using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Authority;

public sealed class GuardianDecisionOutcomeService(
    IGuardianDecisionOutcomeRepository repository,
    IProjectRepository projects)
{
    public async Task RecordDecisionAsync(Guid id, GuardianDecisionDisposition disposition, string reason)
    {
        var record = (await repository.GetPendingAsync()).FirstOrDefault(x => x.Id == id);
        if (record is null) return;
        record.Decision = disposition;
        record.DecisionReason = reason;
        record.DecidedAt = DateTimeOffset.UtcNow;
        if (disposition == GuardianDecisionDisposition.Rejected)
        {
            record.Outcome = GuardianOutcomeDisposition.Superseded;
            record.ObservedAt = DateTimeOffset.UtcNow;
            record.OutcomeEvidence = "The recommendation was rejected by the user.";
        }
        await repository.UpsertAsync(record);
    }

    public async Task RecordOutcomeAsync(Guid id, GuardianOutcomeDisposition outcome, string evidence, int? minutes = null)
    {
        var record = (await repository.GetPendingAsync()).FirstOrDefault(x => x.Id == id);
        if (record is null) return;
        record.Outcome = outcome;
        record.OutcomeEvidence = evidence;
        record.OutcomeMinutes = minutes;
        record.ObservedAt = DateTimeOffset.UtcNow;
        await repository.UpsertAsync(record);
    }

    public async Task ObserveProjectOutcomesAsync(IReadOnlyCollection<GuardianOpenLoop> openLoops)
    {
        var pending = (await repository.GetPendingAsync())
            .Where(x => x.ActionType.Equals("ChangeProjectStatus", StringComparison.OrdinalIgnoreCase) &&
                        x.Decision is GuardianDecisionDisposition.Accepted or GuardianDecisionDisposition.AutoExecuted)
            .ToList();
        foreach (var record in pending)
        {
            if (record.ProjectId is not { } projectId) continue;
            var project = await projects.GetByIdAsync(projectId);
            if (project is null)
            {
                await RecordOutcomeAsync(record.Id, GuardianOutcomeDisposition.Superseded,
                    "The linked project no longer exists.");
                continue;
            }

            var dependencyStillOpen = openLoops.Any(x => x.Id == record.SourceEventId);
            if (!dependencyStillOpen)
            {
                await RecordOutcomeAsync(record.Id, GuardianOutcomeDisposition.Successful,
                    "The linked external-dependency loop subsequently closed.", ElapsedMinutes(record));
            }
            else if (!project.Status.Equals("On Hold", StringComparison.OrdinalIgnoreCase))
            {
                await RecordOutcomeAsync(record.Id, GuardianOutcomeDisposition.Mixed,
                    $"The project returned to '{project.Status}' while the linked dependency was still open.", ElapsedMinutes(record));
            }
        }
    }

    private static int ElapsedMinutes(GuardianDecisionOutcome record) => Math.Max(0, (int)Math.Round(
        (DateTimeOffset.UtcNow - (record.DecidedAt ?? record.RecommendedAt)).TotalMinutes));
}
