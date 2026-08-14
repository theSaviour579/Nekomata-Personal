namespace Nekomata.Integrations.MicrosoftGraph.Models;

public enum CalendarActivityKind
{
    Free,
    Focus,
    Meeting
}

public sealed record CalendarTimelineContext(
    CalendarEvent? Active,
    CalendarEvent? Next,
    CalendarEvent? Then,
    CalendarActivityKind ActiveKind)
{
    public static CalendarTimelineContext Empty { get; } =
        new(null, null, null, CalendarActivityKind.Free);

    public bool HasActiveFocus =>
        Active is not null && ActiveKind == CalendarActivityKind.Focus;
}