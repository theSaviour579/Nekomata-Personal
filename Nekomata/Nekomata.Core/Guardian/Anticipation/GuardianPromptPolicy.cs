namespace Nekomata.Core.Guardian.Anticipation;

public enum GuardianPromptKind { MeetingBrief, WrapUp, Preflight, Chases, NextAction }
public static class GuardianPromptPolicy
{
    public static bool CanPresent(GuardianPromptKind requested, IEnumerable<GuardianPromptKind> waiting,
        bool occupied, bool urgentAttention, DateTimeOffset now, DateTimeOffset pausedUntil, DateTimeOffset quietUntil) =>
        !occupied && !urgentAttention && now >= pausedUntil && now >= quietUntil &&
        !waiting.Any(x => x < requested);
}
