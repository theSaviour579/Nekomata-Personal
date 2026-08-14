using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Changes;

public interface IGuardianProjectChangeValidator
{
    bool CanApply(
        GuardianChange change);
}