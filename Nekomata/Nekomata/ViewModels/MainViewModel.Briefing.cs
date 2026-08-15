using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private async Task RefreshDailyBriefingContextAsync()
    {
        var briefing = Workspace.Briefing;
        try
        {
            var calendar = _services.GetRequiredService<ICalendarService>();
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Today);
            var start = new DateTimeOffset(DateTime.Today, offset);
            var events = (await calendar.GetEventsAsync(start, start.AddDays(1)))
                .OrderBy(item => item.Start)
                .ToList();

            ApplyTodayCalendarCapacity(events);
            var now = DateTimeOffset.Now;
            UpdateLiveTimeline(events, now);
            ScheduleNextCalendarBoundary(events, now);
            CalendarLastSyncedText = $"Calendar synced {DateTime.Now:HH:mm:ss}";

            var meetings = events
                .Where(item => !item.IsNekomataManaged)
                .Where(item => GetOtherParticipants(item).Count > 0)
                .ToList();
            var focusBlocks = events.Count(item =>
                item.IsNekomataManaged ||
                item.Subject.Contains("focus", StringComparison.OrdinalIgnoreCase));
            var bookedMinutes = events
                .Where(item => !item.IsAllDay)
                .Sum(item => Math.Max(0, (int)(item.End - item.Start).TotalMinutes));
            var next = events.FirstOrDefault(item => item.End > DateTimeOffset.Now);

            briefing.CalendarSummary = events.Count == 0
                ? "Calendar: no commitments are scheduled today."
                : $"Calendar: {events.Count} commitment{Plural(events.Count)}, " +
                  $"{meetings.Count} meeting{Plural(meetings.Count)}, {focusBlocks} focus block{Plural(focusBlocks)}, " +
                  $"{FormatMinutes(bookedMinutes)} on the calendar; " +
                  $"{FormatMinutes(Workspace.Capacity.ScheduledMinutesToday)} counts toward workday capacity." +
                  (next is null ? " Today's calendar is complete." : $" Next: {DescribeEvent(next)}.");

            briefing.MeetingSummary = meetings.Count == 0
                ? "Meetings: none with other attendees today."
                : "Meetings: " + string.Join("  •  ", meetings.Select(meeting =>
                    $"{meeting.TimeLabel} {meeting.Subject} with {string.Join(", ", GetOtherParticipants(meeting))}" +
                    (string.IsNullOrWhiteSpace(meeting.Location) ? string.Empty : $" at {meeting.Location}"))) + ".";

            var awareness = new List<string>();
            if (Workspace.Capacity.ExpectedOvertimeMinutes > 0)
                awareness.Add($"{Workspace.Capacity.ExpectedOvertimeMinutes} minutes currently extend beyond capacity");
            if (Workspace.Capacity.BurnoutRisk is "High" or "Critical")
                awareness.Add($"burnout exposure is {Workspace.Capacity.BurnoutRisk.ToLowerInvariant()}");
            if (meetings.Count > 0 && next is not null && meetings.Contains(next))
                awareness.Add($"prepare for {next.Subject} with {string.Join(", ", GetOtherParticipants(next))}");

            briefing.AwarenessSummary = awareness.Count == 0
                ? "Be aware: no immediate conflicts or critical workload warnings are visible."
                : "Be aware: " + string.Join("; ", awareness) + ".";

            OnPropertyChanged(nameof(Workspace));
            OnPropertyChanged(nameof(BriefingSummary));
        }
        catch (Exception ex)
        {
            briefing.CalendarSummary = $"Calendar summary unavailable: {ex.Message}";
            briefing.MeetingSummary = string.Empty;
            briefing.AwarenessSummary = "Be aware: calendar context could not be included in this briefing.";
            OnPropertyChanged(nameof(Workspace));
        }
    }

    private IReadOnlyList<string> GetOtherParticipants(CalendarEvent item)
    {
        var displayName = _personalProfile.Current.DisplayName;
        return item.Attendees
            .Append(item.Organiser)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => string.IsNullOrWhiteSpace(displayName) || !name.Contains(displayName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string DescribeEvent(CalendarEvent item)
    {
        var participants = GetOtherParticipants(item);
        var with = participants.Count == 0 ? string.Empty : $" with {string.Join(", ", participants)}";
        return $"{item.TimeLabel} {item.Subject}{with}";
    }

    private static string FormatMinutes(int minutes) =>
        minutes < 60 ? $"{minutes} min" : $"{minutes / 60}h {minutes % 60:00}m";

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
