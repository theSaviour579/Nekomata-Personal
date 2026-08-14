using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Changes;

public interface IGuardianProjectChangeHandler
{
    Task ApplyAsync(
        GuardianChange change);
}