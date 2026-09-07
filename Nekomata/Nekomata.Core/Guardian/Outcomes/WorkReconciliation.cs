namespace Nekomata.Core.Guardian.Outcomes;

public static class WorkReconciliation
{
    // Do not infer completion from missing records, numeric tenant-specific statuses or cancellation.
    public static bool IsCompletedStatus(string? status) =>
        new[] { "Completed", "Resolved", "Closed" }.Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsNewSentEvidence(string previousId, string currentId, string sender, string ownAddress,
        DateTimeOffset receivedAt, DateTimeOffset reminderCreatedAt) =>
        !string.IsNullOrWhiteSpace(previousId) && !string.IsNullOrWhiteSpace(currentId) && previousId != currentId &&
        !string.IsNullOrWhiteSpace(ownAddress) && sender.Equals(ownAddress, StringComparison.OrdinalIgnoreCase) &&
        receivedAt > reminderCreatedAt;
}
