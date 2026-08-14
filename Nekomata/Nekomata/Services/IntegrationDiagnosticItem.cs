namespace Nekomata.UI.Services;

public sealed record IntegrationDiagnosticItem(
    string Name,
    string Status,
    string Summary,
    string Detail,
    DateTime CheckedAt,
    long DurationMilliseconds)
{
    public bool IsHealthy => Status == "Healthy";
    public bool NeedsAttention => Status == "Attention";
    public string CheckedLabel => $"Checked {CheckedAt:HH:mm:ss} · {DurationMilliseconds} ms";
}
