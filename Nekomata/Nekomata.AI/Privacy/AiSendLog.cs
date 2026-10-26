namespace Nekomata.AI.Privacy;

/// <summary>One call to the AI provider: what kind, how big, and how many values of each kind were hidden. Never the text.</summary>
public sealed record AiSendRecord(DateTimeOffset At, string Channel, string Model, int Characters, IReadOnlyDictionary<string, int> Hidden, bool Refused = false, string Note = "");

/// <summary>The providers report every call here; the desktop and web host write it to a local log for policy checks.</summary>
public static class AiSendLog
{
    public static event Action<AiSendRecord>? Sent;

    public static void Record(AiSendRecord record)
    {
        try { Sent?.Invoke(record); } catch { /* logging must never break a call */ }
    }
}
