using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Tasks;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Core.Guardian.Changes;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianApplyService
    : IGuardianApplyService
{
    private readonly IGuardianActionPipeline
    _pipeline;

    public GuardianApplyService(
    IGuardianActionPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<GuardianApplyResult> ApplyAsync(
     GuardianActionResponse response,
     long? defaultProjectId = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        var result =
            new GuardianApplyResult
            {
                Success = true
            };

        await _pipeline.ExecuteAsync(
            response,
            result);
        result.Success = result.Actions.Count > 0;
        result.Messages.Add(result.Success
            ? $"Applied {result.Actions.Count} action(s)."
            : "No actions were applied.");

        return result;
    }
}