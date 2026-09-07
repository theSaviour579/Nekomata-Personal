using System.Globalization;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed record RetrospectiveObjectivePeriod(DateTime StartedAt, DateTime FinishedAt);

public static class RetrospectiveObjectiveInput
{
    public static bool TryParseDurationToday(string startText,string durationText,DateTime now,
        out RetrospectiveObjectivePeriod? period,out string error)
    {
        period=null;
        if(!TimeOnly.TryParseExact(startText.Trim(),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
        { error="Enter a start time in 24-hour format, for example 14:30."; return false; }
        if(!int.TryParse(durationText,out var minutes) || minutes<=0 || minutes>1440)
        { error="Enter a duration between 1 and 1440 whole minutes."; return false; }
        var start=now.Date.Add(time.ToTimeSpan());
        var finish=start.AddMinutes(minutes);
        return TryParseToday(start.ToString("g",CultureInfo.CurrentCulture),finish.ToString("g",CultureInfo.CurrentCulture),
            now.Date,now,out period,out error);
    }

    public static bool TryParseToday(
        string startedText,
        string finishedText,
        DateTime today,
        DateTime now,
        out RetrospectiveObjectivePeriod? period,
        out string error)
    {
        period = null;
        error = "";

        if (!DateTime.TryParse(startedText, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var started) ||
            !DateTime.TryParse(finishedText, CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var finished))
        {
            error = "Enter both dates and times, for example 02/09/2026 14:30.";
            return false;
        }

        if (started.Date != today.Date || finished.Date != today.Date)
        {
            error = "This digest accepts work completed today. Use today's date in both fields.";
            return false;
        }

        if (finished <= started)
        {
            error = "Finish time must be after the start time.";
            return false;
        }

        if (finished > now.AddMinutes(1))
        {
            error = "Finish time cannot be in the future.";
            return false;
        }

        period = new RetrospectiveObjectivePeriod(started, finished);
        return true;
    }
}
