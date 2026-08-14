using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianApplyService
{
    Task<GuardianApplyResult> ApplyAsync(
        GuardianActionResponse response,
        long? defaultProjectId = null);
}