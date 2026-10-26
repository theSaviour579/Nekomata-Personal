namespace Nekomata.Core.Guardian.OpenLoops;

public static class GuardianOpenLoopCommandIntent
{
    public static bool IsPrepareChases(string message)
    {
        var value = Normalise(message);
        var asksToAct = value.Contains("deal with") || value.Contains("prepare") || value.Contains("draft");
        var waitingScope = value.Contains("waiting on") || value.Contains("waiting for") ||
                           value.Contains("chasers") || value.Contains("follow ups");
        var asksWhatNeedsChasing = value.Contains("what needs chasing") ||
                                   value.Contains("who needs chasing") ||
                                   value.Contains("anything need chasing") ||
                                   value.Contains("anything needs chasing");
        return (asksToAct && waitingScope) || asksWhatNeedsChasing;
    }

    public static bool IsWaitingQuery(string message)
    {
        var value = Normalise(message);
        var asksWhatOrWho = value.Contains("what am i") || value.Contains("who am i") ||
                            value.Contains("what are we") || value.Contains("who are we");
        return (asksWhatOrWho && (value.Contains("waiting on") || value.Contains("waiting for"))) ||
               value.Contains("open loops");
    }

    public static bool IsApproveChases(string message)
    {
        var value = Normalise(message);
        return value.Contains("approve") && (value.Contains("chaser") || value.Contains("follow up"));
    }

    public static bool IsSendChases(string message)
    {
        var value = Normalise(message);
        return value.Contains("send") && value.Contains("approved") &&
               (value.Contains("chaser") || value.Contains("follow up"));
    }

    private static string Normalise(string message) => message
        .Trim().ToLowerInvariant()
        .Replace('’', '\'')
        .Replace("follow-up", "follow up")
        .Replace("followups", "follow ups");
}
