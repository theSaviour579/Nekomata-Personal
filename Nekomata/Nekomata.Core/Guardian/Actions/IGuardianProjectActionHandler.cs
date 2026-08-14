using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianProjectActionHandler
{
    Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result);
}