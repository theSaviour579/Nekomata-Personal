using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianTaskActionHandler
{
    Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result);
}