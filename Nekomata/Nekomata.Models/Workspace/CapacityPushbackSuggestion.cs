namespace Nekomata.Models.Workspace;

public sealed class CapacityPushbackSuggestion
{
    public string Title { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public int Score { get; init; }
    public int MinutesRecovered { get; init; }
    public string Reason { get; init; } = string.Empty;
}