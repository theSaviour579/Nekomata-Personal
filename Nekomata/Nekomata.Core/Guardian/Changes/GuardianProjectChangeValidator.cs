using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Changes;

public class GuardianProjectChangeValidator
    : IGuardianProjectChangeValidator
{
    private static readonly HashSet<string>
        SupportedProperties =
        [
            "Priority"
        ];

    public bool CanApply(
        GuardianChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return SupportedProperties.Contains(
            change.Property,
            StringComparer.OrdinalIgnoreCase);
    }
}