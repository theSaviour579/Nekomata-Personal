using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianActionHandler
{
    bool CanHandle(
        GuardianActionResponse response);

    Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result);
}