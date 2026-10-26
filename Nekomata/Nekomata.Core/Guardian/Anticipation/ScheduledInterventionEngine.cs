using System.Security.Cryptography;
using System.Text;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed class ScheduledInterventionEngine
{
    public IReadOnlyList<ScheduledIntervention> Reconcile(IEnumerable<AnticipatedAction> predictions,
        IEnumerable<ScheduledIntervention> previous, DateTimeOffset now)
    {
        var old = previous.ToDictionary(x => x.Id);
        var current = predictions.Where(IsActive).Select(x => FromPrediction(x, now, old)).ToList();
        var currentIds = current.Select(x => x.Id).ToHashSet();

        current.AddRange(old.Values.Where(x => !currentIds.Contains(x.Id) &&
                x.Status is not ScheduledInterventionStatus.Completed and not ScheduledInterventionStatus.Cancelled and
                    not ScheduledInterventionStatus.Superseded)
            .Select(x => Supersede(x, now)));

        return current.OrderBy(x => x.Status == ScheduledInterventionStatus.Superseded)
            .ThenBy(x => x.TriggerAt).ThenBy(x => x.Title).ToList();
    }

    private static ScheduledIntervention FromPrediction(AnticipatedAction value, DateTimeOffset now,
        IReadOnlyDictionary<Guid, ScheduledIntervention> previous)
    {
        var id = StableId($"scheduled:{value.Id:N}");
        var status = Status(value, now);
        return new ScheduledIntervention
        {
            Id = id, PredictionId = value.Id, WorkThreadId = value.WorkThreadId,
            ActionType = value.ProposedAction, Title = HumanTitle(value.ProposedAction), Reason = value.Reason,
            Evidence = value.Evidence.ToList(), Authority = value.Authority, Status = status,
            ScheduledAt = previous.TryGetValue(id, out var prior) ? prior.ScheduledAt : now,
            TriggerAt = value.ExecuteAfter ?? now, ExpiresAt = value.ExpiresAt,
            PreparedContent = value.PreparedContent,
            CompletedAt = status == ScheduledInterventionStatus.Completed ? value.SentAt ?? now : null,
            Outcome = value.LastSendError
        };
    }

    private static ScheduledInterventionStatus Status(AnticipatedAction value, DateTimeOffset now) => value.Status switch
    {
        AnticipatedActionStatus.Prepared => ScheduledInterventionStatus.Prepared,
        AnticipatedActionStatus.ApprovalRequired or AnticipatedActionStatus.Approved => ScheduledInterventionStatus.AwaitingApproval,
        AnticipatedActionStatus.Sent or AnticipatedActionStatus.Completed => ScheduledInterventionStatus.Completed,
        AnticipatedActionStatus.Cancelled => ScheduledInterventionStatus.Cancelled,
        _ when (value.ExecuteAfter ?? now) <= now => ScheduledInterventionStatus.Due,
        _ => ScheduledInterventionStatus.Scheduled
    };

    private static bool IsActive(AnticipatedAction value) => value.Status is not AnticipatedActionStatus.Expired;

    private static ScheduledIntervention Supersede(ScheduledIntervention value, DateTimeOffset now) => new()
    {
        Id = value.Id, PredictionId = value.PredictionId, WorkThreadId = value.WorkThreadId,
        ActionType = value.ActionType, Title = value.Title, Reason = value.Reason, Evidence = value.Evidence,
        Authority = value.Authority, Status = ScheduledInterventionStatus.Superseded,
        ScheduledAt = value.ScheduledAt, TriggerAt = value.TriggerAt, ExpiresAt = value.ExpiresAt,
        CompletedAt = now, PreparedContent = value.PreparedContent,
        Outcome = "The source prediction is no longer valid after reconciliation."
    };

    private static string HumanTitle(string value) => value switch
    {
        "PrepareMeetingContext" => "Prepare meeting context",
        "CheckReadinessGate" => "Check readiness gate",
        "PrepareDependencyFollowUp" => "Prepare dependency follow-up",
        "WaitForDependency" => "Reconsider waiting dependency",
        "ContinueActiveTask" => "Continue active task",
        _ => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? $" {char.ToLowerInvariant(c)}" : c.ToString()))
    };

    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
}
