using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public interface IGuardianCalendarActionHandler
{
    Task ApplyAsync(GuardianActionResponse response, GuardianApplyResult result);
}
