namespace Nekomata.Integrations.MicrosoftGraph.Models;

public static class CalendarTimelineContextResolver
{
    public static CalendarTimelineContext Resolve(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);

        var remaining = events
            .Where(item => !item.IsAllDay && item.End > now)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .ToList();

        var active = remaining.FirstOrDefault(item =>
            item.Start <= now && item.End > now);
        var upcoming = remaining
            .Where(item => item.Start > now)
            .Take(2)
            .ToList();

        return new CalendarTimelineContext(
            active,
            upcoming.ElementAtOrDefault(0),
            upcoming.ElementAtOrDefault(1),
            Classify(active));
    }

    public static CalendarActivityKind Classify(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
            return CalendarActivityKind.Free;

        return IsFocusBlock(calendarEvent)
            ? CalendarActivityKind.Focus
            : CalendarActivityKind.Meeting;
    }

    public static bool IsFocusBlock(CalendarEvent? calendarEvent)
    {
        if (calendarEvent is null)
            return false;
        var subject = calendarEvent.Subject.TrimStart();
        if (subject.StartsWith("Focus", StringComparison.OrdinalIgnoreCase))
            return true;

        if (subject.Contains("meeting", StringComparison.OrdinalIgnoreCase))
            return false;

        return calendarEvent.IsNekomataManaged;
    }
}