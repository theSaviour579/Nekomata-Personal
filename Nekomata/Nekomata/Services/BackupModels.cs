namespace Nekomata.UI.Services;

public sealed record BackupStatus(
    bool ToolingAvailable,
    string ToolingDetail,
    DateTime? LatestBackupAt,
    string? LatestBackupPath,
    bool AutomaticConfigured)
{
    public bool IsFresh => LatestBackupAt is not null && DateTime.Now - LatestBackupAt.Value <= TimeSpan.FromHours(36);
    public string FreshnessLabel => LatestBackupAt is null ? "No backup found" : $"Last backup {LatestBackupAt.Value:g}";
}

public sealed record BackupOperationResult(bool Success, string Message, string? Path = null);
