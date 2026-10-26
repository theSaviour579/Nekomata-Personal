using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public sealed record HomeDayItem(string Day, string Date, double MeetingWidth, double FocusWidth, double FreeWidth,
    string Hours, string Detail, bool IsToday);

/// <summary>The home page's status colours.</summary>
internal static class HomePalette
{
    public static readonly Brush Critical = Make("#FF5A72");
    public static readonly Brush Warning = Make("#FFC857");
    public static readonly Brush Ok = Make("#46F0B4");
    public static readonly Brush Purple = Make("#9C85FF");
    public static readonly Brush Cyan = Make("#42D9FF");
    public static readonly Brush Muted = Make("#8FA4B8");
    public static readonly Brush Text = Make("#EAF4FF");

    private static Brush Make(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

// The week ahead on the home page: meetings, focus and free time over the next five working days.
public partial class MainViewModel
{
    private IReadOnlyList<CalendarEvent> _homeWeek = [];
    private DateTimeOffset _homeWeekLoaded = DateTimeOffset.MinValue;

    [ObservableProperty] private string homeWeekSummary = "";

    public ObservableCollection<HomeDayItem> HomeWeek { get; } = [];

    private async Task RefreshHomeWeekAsync(DateTimeOffset now)
    {
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        // After the working day (or at a weekend) the week starts from the next working day.
        var weekStart = now.LocalDateTime >= settings.GetEnd(now.Date) || now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? WrapUpPlanning.NextWorkday(now.Date) : now.Date;
        var days = new List<DateTime> { weekStart };
        while (days.Count < 5) days.Add(WrapUpPlanning.NextWorkday(days[^1]));
        if (now - _homeWeekLoaded > TimeSpan.FromMinutes(15))
        {
            var start = new DateTimeOffset(days[0], TimeZoneInfo.Local.GetUtcOffset(days[0]));
            var end = new DateTimeOffset(days[^1].AddDays(1), TimeZoneInfo.Local.GetUtcOffset(days[^1]));
            _homeWeek = await SafeAsync<IReadOnlyList<CalendarEvent>>(() =>
                _services.GetRequiredService<ICalendarService>().GetEventsAsync(start, end), Array.Empty<CalendarEvent>());
            _homeWeekLoaded = now;
        }

        HomeWeek.Clear();
        const double barWidth = 180;
        double totalMeetings = 0, totalFree = 0;
        foreach (var day in days)
        {
            var dayStart = settings.GetStart(day);
            var dayEnd = settings.GetEnd(day);
            var workMinutes = Math.Max(1, (dayEnd - dayStart).TotalMinutes);
            var events = _homeWeek.Where(e => !e.IsAllDay && e.Start.LocalDateTime < dayEnd && e.End.LocalDateTime > dayStart).ToList();
            double Minutes(IEnumerable<CalendarEvent> list) => Merge(list.Select(e =>
                (Max(e.Start.LocalDateTime, dayStart), Min(e.End.LocalDateTime, dayEnd))));
            var meetings = events.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0).ToList();
            var meetingMinutes = Minutes(meetings);
            var focusMinutes = Math.Max(0, Minutes(events) - meetingMinutes - Minutes(events.Where(e => !e.IsNekomataManaged && e.Attendees.Count == 0)));
            var busyMinutes = Minutes(events);
            var freeMinutes = Math.Max(0, workMinutes - busyMinutes);
            totalMeetings += meetingMinutes;
            totalFree += freeMinutes;
            var first = meetings.OrderBy(e => e.Start).FirstOrDefault();
            var detail = meetings.Count == 0 ? "No meetings"
                : $"{meetings.Count} meeting{(meetings.Count == 1 ? "" : "s")} · first {first!.Start.LocalDateTime:HH:mm} {CleanCalendarObjectiveTitle(first.Subject)}";
            HomeWeek.Add(new HomeDayItem(
                day == now.Date ? "Today" : day == now.Date.AddDays(1) ? "Tomorrow" : day.ToString("dddd"), day.ToString("d MMM"),
                meetingMinutes / workMinutes * barWidth, focusMinutes / workMinutes * barWidth, freeMinutes / workMinutes * barWidth,
                $"{H(meetingMinutes)} meetings · {H(focusMinutes)} focus · {H(freeMinutes)} free", detail, day == now.Date));
        }
        var busiest = HomeWeek.OrderByDescending(d => d.MeetingWidth).FirstOrDefault();
        HomeWeekSummary = $"{H(totalMeetings)} of meetings and {H(totalFree)} unbooked over the next five working days" +
                          (busiest is { MeetingWidth: > 0 } ? $" · busiest: {busiest.Day}" : "");

        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        static string H(double minutes) => minutes < 60 ? $"{minutes:0}m" : $"{Math.Floor(minutes / 60):0}h{(minutes % 60 >= 1 ? $" {minutes % 60:0}m" : "")}";
        // Total minutes covered by a set of ranges, counting overlaps once.
        static double Merge(IEnumerable<(DateTime Start, DateTime End)> ranges)
        {
            double total = 0;
            DateTime? curStart = null, curEnd = null;
            foreach (var (s, e) in ranges.Where(r => r.End > r.Start).OrderBy(r => r.Start))
            {
                if (curEnd is null || s > curEnd) { if (curEnd is not null) total += (curEnd.Value - curStart!.Value).TotalMinutes; curStart = s; curEnd = e; }
                else if (e > curEnd) curEnd = e;
            }
            if (curEnd is not null) total += (curEnd.Value - curStart!.Value).TotalMinutes;
            return total;
        }
    }
}
