using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public static class GuardianTaskChangeNormalizer
{
    private static readonly string[] SupportedStatuses =
        ["Completed", "Cancelled", "Open"];

    public static bool TryNormalizeStatus(
        GuardianChange change,
        out string status,
        out string? note)
    {
        ArgumentNullException.ThrowIfNull(change);

        status = string.Empty;
        note = null;

        if (!change.Property.Equals("Status", StringComparison.OrdinalIgnoreCase))
            return false;

        var value = change.NewValue.Trim();
        status = SupportedStatuses.FirstOrDefault(candidate =>
            value.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

        if (status.Length == 0)
            return false;

        if (value.Length > status.Length)
        {
            note = value[status.Length..]
                .Trim()
                .TrimStart('-', '–', '—', ':')
                .Trim();

            if (note.Length == 0)
                note = null;
        }

        return true;
    }
}