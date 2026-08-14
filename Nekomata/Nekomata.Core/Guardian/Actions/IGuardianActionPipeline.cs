using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianActionPipeline
{
    Task ExecuteAsync(
        GuardianActionResponse response,
        GuardianApplyResult result);
}