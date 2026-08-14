using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Tasks;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Core.Guardian.Changes;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianApplyService
    : IGuardianApplyService
{
    private readonly IGuardianActionPipeline
    _pipeline;
    private readonly IGuardianAuditRepository? _auditRepository;

    public GuardianApplyService(
    IGuardianActionPipeline pipeline,
    IGuardianAuditRepository? auditRepository = null)
    {
        _pipeline = pipeline;
        _auditRepository = auditRepository;
    }

    public async Task<GuardianApplyResult> ApplyAsync(
     GuardianActionResponse response,
     long? defaultProjectId = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.ProjectId ??= defaultProjectId;

        var result =
            new GuardianApplyResult
            {
                Success = true
            };

        await _pipeline.ExecuteAsync(
            response,
            result);
        if (result.Actions.Count > 0 && _auditRepository is not null)
        {
            var batchId = Guid.NewGuid();
            var appliedAt = DateTime.UtcNow;
            var entries = result.Actions.Select(action => new GuardianAuditEntry
            {
                BatchId = batchId,
                Operation = action.Operation,
                EntityType = action.Type,
                EntityId = action.EntityId,
                ExternalId = action.ExternalId,
                Title = action.Title,
                Description = action.Description,
                Reason = action.Reason,
                Confidence = Math.Clamp(action.Confidence, 0, 100),
                BeforeState = action.BeforeState,
                AfterState = action.AfterState,
                Reversible = action.Reversible,
                IrreversibleReason = action.IrreversibleReason,
                AppliedAt = appliedAt
            }).ToList();

            try
            {
                await _auditRepository.AddBatchAsync(entries);
                result.AuditBatchId = batchId;
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Audit warning: actions were applied but their history could not be saved ({ex.Message}).");
            }
        }
        result.Success = result.Actions.Count > 0;
        result.Messages.Add(result.Success
            ? $"Applied {result.Actions.Count} action(s)."
            : "No actions were applied.");

        return result;
    }
}
