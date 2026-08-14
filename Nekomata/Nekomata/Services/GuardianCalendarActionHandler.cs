using System.Globalization;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Actions;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using System.Text.Json;

namespace Nekomata.UI.Services;

public sealed class GuardianCalendarActionHandler : IGuardianCalendarActionHandler
{
    private static readonly TimeSpan MinimumCreatedBlock = TimeSpan.FromMinutes(10);
    private readonly ICalendarService _calendar;
    private readonly CalendarUndoService _undo;

    public GuardianCalendarActionHandler(ICalendarService calendar, CalendarUndoService undo)
    {
        _calendar = calendar;
        _undo = undo;
    }

    public async Task ApplyAsync(GuardianActionResponse response, GuardianApplyResult result)
    {
        var changes = response.Changes
            .Where(change => change.Selected &&
                change.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (changes.Count > 0) _undo.BeginBatch();

        foreach (var change in changes)
        {
            try
            {
                var operation = new string(change.Property.Where(char.IsLetterOrDigit).ToArray())
                    .ToLowerInvariant();
                switch (operation)
                {
                    case "createfocusblock":
                    case "createcalendarblock":
                    case "schedulefocusblock":
                        await CreateAsync(change, result);
                        break;
                    case "movenekomatablock":
                    case "movefocusblock":
                        await MoveAsync(change, result);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported operation '{change.Property}'.");
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"⚠ Skipped {Describe(change)}: {ex.Message}");
            }
        }

        if (changes.Count > 0) _undo.CommitBatch();

        if (changes.Count > 0 && result.Actions.All(action => action.Type != "Calendar"))
            result.Success = false;
    }

    private async Task CreateAsync(GuardianChange change, GuardianApplyResult result)
    {
        var parts = Parse(change.NewValue, 3);
        var requestedStart = ParseDate(parts[0]);
        var requestedEnd = ParseDate(parts[1]);
        var title = parts[2].Trim();
        ValidateRange(requestedStart, requestedEnd);

        var fitted = await FitIntoFreeWindowAsync(requestedStart, requestedEnd);
        var haloTicket = System.Text.RegularExpressions.Regex.Match(title, @"#(?<id>\d+)");
        var marker = haloTicket.Success
            ? $"NEKOMATA:HALO:{haloTicket.Groups["id"].Value}"
            : change.EntityId > 0
                ? $"NEKOMATA:TASK:{change.EntityId}"
                : $"NEKOMATA:GUARDIAN:{Guid.NewGuid():N}";
        var created = await _calendar.CreateFocusEventAsync(title, fitted.Start, fitted.End, marker);
        _undo.RecordCreated(created.Id, title, fitted.Start);

        result.Actions.Add(new GuardianAppliedAction
        {
            Type = "Calendar",
            Title = $"Scheduled {title}",
            Description = $"{fitted.Start:ddd dd MMM HH:mm}–{fitted.End:HH:mm}",
            EntityId = change.EntityId > 0 ? change.EntityId : null,
            ExternalId = created.Id,
            Operation = "Create",
            AfterState = JsonSerializer.Serialize(CalendarAuditSnapshot.From(created)),
            Reversible = !string.IsNullOrWhiteSpace(created.Id),
            IrreversibleReason = string.IsNullOrWhiteSpace(created.Id) ? "Microsoft 365 did not return an event ID." : null,
            Reason = change.Reason,
            Confidence = change.Confidence
        });

        if (fitted.Start != requestedStart || fitted.End != requestedEnd)
        {
            result.Messages.Add(
                $"ℹ Adjusted '{title}' from {requestedStart:HH:mm}–{requestedEnd:HH:mm} " +
                $"to the free window {fitted.Start:HH:mm}–{fitted.End:HH:mm}.");
        }
    }

    private async Task MoveAsync(GuardianChange change, GuardianApplyResult result)
    {
        var parts = Parse(change.NewValue, 2);
        var start = ParseDate(parts[0]);
        var end = ParseDate(parts[1]);
        ValidateRange(start, end);
        await EnsureNoProtectedConflictAsync(start, end, change.OldValue);
        var existing = await FindEventAsync(change.OldValue, start);
        var moved = await _calendar.MoveFocusEventAsync(change.OldValue, start, end);
        result.Actions.Add(new GuardianAppliedAction
        {
            Type = "Calendar",
            Title = $"Moved {moved.Subject}",
            Description = $"{start:ddd dd MMM HH:mm}–{end:HH:mm}",
            ExternalId = moved.Id,
            Operation = "Move",
            BeforeState = existing is null ? null : JsonSerializer.Serialize(CalendarAuditSnapshot.From(existing)),
            AfterState = JsonSerializer.Serialize(CalendarAuditSnapshot.From(moved)),
            Reversible = existing is not null && !string.IsNullOrWhiteSpace(moved.Id),
            IrreversibleReason = existing is null ? "The original calendar position could not be verified." : null,
            Reason = change.Reason,
            Confidence = change.Confidence
        });
    }

    private async Task<CalendarEvent?> FindEventAsync(string eventId, DateTimeOffset around)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(around.Date);
        var rangeStart = new DateTimeOffset(around.Date.AddDays(-7), offset);
        var rangeEnd = new DateTimeOffset(around.Date.AddDays(8), offset);
        return (await _calendar.GetEventsAsync(rangeStart, rangeEnd)).FirstOrDefault(item => item.Id == eventId);
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> FitIntoFreeWindowAsync(
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd)
    {
        var events = await GetEventsForRangeAsync(requestedStart, requestedEnd);
        var occupied = events
            .Where(item => item.IsAllDay || (item.Start < requestedEnd && item.End > requestedStart))
            .OrderBy(item => item.Start)
            .ToList();

        var free = new List<(DateTimeOffset Start, DateTimeOffset End)>
        {
            (requestedStart, requestedEnd)
        };

        foreach (var item in occupied)
        {
            var busyStart = item.IsAllDay ? requestedStart : item.Start;
            var busyEnd = item.IsAllDay ? requestedEnd : item.End;
            free = free.SelectMany(segment => Subtract(segment, busyStart, busyEnd)).ToList();
        }

        var best = free
            .Where(segment => segment.End - segment.Start >= MinimumCreatedBlock)
            .OrderByDescending(segment => segment.End - segment.Start)
            .ThenBy(segment => segment.Start)
            .Select(segment => ((DateTimeOffset Start, DateTimeOffset End)?)segment)
            .FirstOrDefault();

        if (best is null)
        {
            var blockers = string.Join(", ", occupied.Select(item => $"'{item.Subject}'").Distinct());
            throw new InvalidOperationException(
                blockers.Length == 0
                    ? "No usable time remains in the proposed window."
                    : $"No free block of at least {MinimumCreatedBlock.TotalMinutes:0} minutes remains because it overlaps {blockers}.");
        }

        return best.Value;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Subtract(
        (DateTimeOffset Start, DateTimeOffset End) segment,
        DateTimeOffset busyStart,
        DateTimeOffset busyEnd)
    {
        if (busyEnd <= segment.Start || busyStart >= segment.End)
        {
            yield return segment;
            yield break;
        }

        if (busyStart > segment.Start)
            yield return (segment.Start, busyStart);
        if (busyEnd < segment.End)
            yield return (busyEnd, segment.End);
    }

    private async Task EnsureNoProtectedConflictAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        string? movingEventId = null)
    {
        var events = await GetEventsForRangeAsync(start, end);
        var conflict = events.FirstOrDefault(item =>
            item.Id != movingEventId &&
            (item.IsAllDay || (item.Start < end && item.End > start)) &&
            !item.IsNekomataManaged);
        if (conflict is not null)
            throw new InvalidOperationException($"'{conflict.Subject}' is a protected calendar event and overlaps this time.");
    }

    private async Task<IReadOnlyList<CalendarEvent>> GetEventsForRangeAsync(
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(start.Date);
        var dayStart = new DateTimeOffset(start.Date, offset);
        var rangeEnd = end.Date > start.Date ? new DateTimeOffset(end.Date.AddDays(1), offset) : dayStart.AddDays(1);
        return await _calendar.GetEventsAsync(dayStart, rangeEnd);
    }

    private static string Describe(GuardianChange change)
    {
        var parts = change.NewValue.Split('|', StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? $"'{parts[2]}'" : change.Property;
    }

    private static string[] Parse(string value, int count)
    {
        var parts = value.Split('|', count, StringSplitOptions.TrimEntries);
        if (parts.Length != count || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("The proposed value must use start|end|title.");
        return parts;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Invalid calendar time '{value}'.");

    private static void ValidateRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
            throw new InvalidOperationException("The block must end after it starts.");
        if (end - start > TimeSpan.FromHours(8))
            throw new InvalidOperationException("A focus block cannot be longer than eight hours.");
    }

    private sealed record CalendarAuditSnapshot(string Id, string Subject, DateTimeOffset Start, DateTimeOffset End)
    {
        public static CalendarAuditSnapshot From(CalendarEvent item) => new(item.Id, item.Subject, item.Start, item.End);
    }
}
