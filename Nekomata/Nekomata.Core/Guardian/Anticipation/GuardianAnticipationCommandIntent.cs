namespace Nekomata.Core.Guardian.Anticipation;

public static class GuardianAnticipationCommandIntent
{
    public static bool IsNextMeetingPreparationQuery(string message)
    {
        var value = message.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        return value is "prepare me for my next meeting" or "prepare me for the next meeting" or
            "brief me for my next meeting" or "what do i need for my next meeting";
    }

    public static bool IsNextActionQuery(string message)
    {
        var value = message.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        return value.StartsWith("what happens next for ") || value is "what can move forward now" or
            "what is correctly waiting" or "what's correctly waiting" or "show next actions";
    }

    public static bool IsQueueQuery(string message)
    {
        var value = message.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        return value is "what are you anticipating" or "what are you preparing" or
            "show anticipated actions" or "what do you think i need next" or "what happens next";
    }

    public static bool IsInterventionScheduleQuery(string message)
    {
        var value = message.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        return value is "what will you handle next" or "what are you reconsidering today" or
            "what have you scheduled on my behalf" or "why will guardian act on this" or
            "show scheduled interventions";
    }

    public static bool IsCalibrationQuery(string message)
    {
        var value = message.Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        return value is "how is guardian performing" or "how accurate are your predictions" or
            "what did you get wrong" or "how much admin time have you saved" or
            "show prediction calibration";
    }
}
