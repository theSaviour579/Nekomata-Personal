namespace Nekomata.Core.Guardian.Autopilot;

/// <summary>A calendar entry as the autopilot sees it. Only Guardian's own blocks are ever moved.</summary>
public sealed record AutopilotEvent(
    string Id, string Subject, DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, bool IsGuardianBlock);

public sealed record AutopilotMove(
    string EventId, string Subject, DateTimeOffset OldStart, DateTimeOffset OldEnd,
    DateTimeOffset NewStart, DateTimeOffset NewEnd, string Reason, bool Shortened);

public sealed record AutopilotUnplaced(string EventId, string Subject, string Reason);

public sealed record AutopilotPlan(IReadOnlyList<AutopilotMove> Moves, IReadOnlyList<AutopilotUnplaced> Unplaced)
{
    public bool IsEmpty => Moves.Count == 0 && Unplaced.Count == 0;
}

public sealed class AutopilotOptions
{
    /// <summary>Breathing room kept after meetings before a focus block starts.</summary>
    public TimeSpan MeetingBuffer { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>A block may be shortened to this, but no further, to keep it today.</summary>
    public TimeSpan MinimumBlock { get; init; } = TimeSpan.FromMinutes(25);
    public TimeSpan Snap { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Hours the user has shown he doesn't want focus blocks moved into; used only when there is another choice.</summary>
    public IReadOnlySet<int> AvoidHours { get; init; } = new HashSet<int>();
}

/// <summary>
/// Keeps Guardian's own focus blocks workable as the day changes. Deterministic and side-effect free: it returns the
/// moves, and the caller applies them through the audited, undoable calendar path.
///
/// A future Guardian block is moved when it clashes with anything else in the calendar (a meeting added, a personal
/// appointment, lunch) or runs past the end of the working day. A block that has already passed is moved when the
/// caller says it was missed. A running block can be pushed on request. Everything else - other people's meetings,
/// the user's own appointments, the block he is in - is fixed. New slots are the free gap closest to where the block
/// was, never before now, and blocks may be shortened to the minimum rather than dropped. What doesn't fit is reported.
/// </summary>
public static class DayAutopilotPlanner
{
    public static AutopilotPlan Replan(
        IEnumerable<AutopilotEvent> events,
        DateTimeOffset now,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        IReadOnlySet<string>? missedBlockIds = null,
        IReadOnlySet<string>? pushBlockIds = null,
        IReadOnlySet<string>? handsOffIds = null,
        AutopilotOptions? options = null)
    {
        options ??= new AutopilotOptions();
        missedBlockIds ??= new HashSet<string>();
        pushBlockIds ??= new HashSet<string>();
        handsOffIds ??= new HashSet<string>();
        var all = events.Where(e => !e.IsAllDay && e.End > e.Start).ToList();
        var earliest = Snap(now > dayStart ? now : dayStart, options.Snap);
        if (earliest >= dayEnd) return new([], []);

        var fixedEvents = all.Where(e => !e.IsGuardianBlock && e.End > earliest && e.Start < dayEnd).ToList();
        var blocks = all.Where(e => e.IsGuardianBlock && !handsOffIds.Contains(e.Id)).ToList();

        var toMove = new List<(AutopilotEvent Block, string Reason, DateTimeOffset NotBefore)>();
        foreach (var block in blocks.OrderBy(b => b.Start))
        {
            var running = block.Start <= now && block.End > now;
            if (pushBlockIds.Contains(block.Id))
                toMove.Add((block, "you asked me to push it", running ? block.End : earliest));
            else if (block.End <= now)
            {
                if (missedBlockIds.Contains(block.Id)) toMove.Add((block, "you didn't get to it", earliest));
            }
            else if (!running)
            {
                var clash = fixedEvents.FirstOrDefault(f => f.Start < block.End && f.End > block.Start);
                if (clash is not null) toMove.Add((block, $"{Short(clash.Subject)} is now in that slot", earliest));
                else if (block.End > dayEnd) toMove.Add((block, "it ran past the end of your day", earliest));
            }
        }
        if (toMove.Count == 0) return new([], []);

        var movingIds = toMove.Select(m => m.Block.Id).ToHashSet();
        var busy = fixedEvents.Select(f => (f.Start, End: f.End + options.MeetingBuffer))
            .Concat(blocks.Where(b => !movingIds.Contains(b.Id) && b.End > earliest).Select(b => (b.Start, b.End)))
            .ToList();

        var moves = new List<AutopilotMove>();
        var unplaced = new List<AutopilotUnplaced>();
        foreach (var (block, reason, notBefore) in toMove)
        {
            var duration = block.End - block.Start;
            var from = Snap(notBefore > earliest ? notBefore : earliest, options.Snap);
            var gaps = FreeGaps(busy, from, dayEnd);
            var target = block.Start > from ? block.Start : from;

            (DateTimeOffset Start, DateTimeOffset End)? slot = gaps
                .Where(g => g.End - g.Start >= duration)
                .Select(g =>
                {
                    var start = Clamp(Snap(target, options.Snap), g.Start, g.End - duration);
                    return (Start: start, End: start + duration);
                })
                .OrderBy(s => options.AvoidHours.Contains(s.Start.Hour) ? 1 : 0)
                .ThenBy(s => Math.Abs((s.Start - target).TotalMinutes))
                .ThenBy(s => s.Start)
                .Select(s => ((DateTimeOffset, DateTimeOffset)?)s)
                .FirstOrDefault();
            var shortened = false;
            if (slot is null)
            {
                var gap = gaps.Where(x => x.End - x.Start >= options.MinimumBlock)
                    .OrderByDescending(x => x.End - x.Start).ThenBy(x => x.Start)
                    .Select(x => ((DateTimeOffset Start, DateTimeOffset End)?)x)
                    .FirstOrDefault();
                if (gap is { } largest)
                {
                    slot = (largest.Start, largest.End);
                    shortened = true;
                }
            }

            if (slot is not { } chosen)
            {
                unplaced.Add(new(block.Id, block.Subject, "there's no room left today"));
                continue;
            }
            if (chosen.Start == block.Start && chosen.End == block.End) continue;
            moves.Add(new(block.Id, block.Subject, block.Start, block.End, chosen.Start, chosen.End, reason, shortened));
            busy.Add((chosen.Start, chosen.End));
        }
        return new(moves, unplaced);

    }

    /// <summary>
    /// The earliest free slot of the given length between from and until, inside the working day, keeping the meeting
    /// buffer after other people's events. Used to book time for new work (promises) without touching anything.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End)? FindSlot(
        IEnumerable<AutopilotEvent> events, DateTimeOffset from, DateTimeOffset until,
        DateTimeOffset dayStart, DateTimeOffset dayEnd, TimeSpan duration, AutopilotOptions? options = null)
    {
        options ??= new AutopilotOptions();
        var start = Snap(from > dayStart ? from : dayStart, options.Snap);
        var end = until < dayEnd ? until : dayEnd;
        if (end - start < duration) return null;
        var busy = events.Where(e => !e.IsAllDay && e.End > e.Start)
            .Select(e => (e.Start, End: e.IsGuardianBlock ? e.End : e.End + options.MeetingBuffer));
        foreach (var gap in FreeGaps(busy, start, end))
            if (gap.End - gap.Start >= duration) return (gap.Start, gap.Start + duration);
        return null;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> FreeGaps(
        IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> busy, DateTimeOffset from, DateTimeOffset to)
    {
        var gaps = new List<(DateTimeOffset, DateTimeOffset)>();
        var cursor = from;
        foreach (var (start, end) in busy.Where(b => b.End > from && b.Start < to).OrderBy(b => b.Start))
        {
            if (start > cursor) gaps.Add((cursor, start < to ? start : to));
            if (end > cursor) cursor = end;
            if (cursor >= to) break;
        }
        if (cursor < to) gaps.Add((cursor, to));
        return gaps;
    }

    private static DateTimeOffset Snap(DateTimeOffset value, TimeSpan step)
    {
        if (step <= TimeSpan.Zero) return value;
        var ticks = value.Ticks % step.Ticks;
        return ticks == 0 ? value : value.AddTicks(step.Ticks - ticks);
    }

    private static DateTimeOffset Clamp(DateTimeOffset value, DateTimeOffset min, DateTimeOffset max) =>
        value < min ? min : value > max ? max : value;

    private static string Short(string subject)
    {
        var value = subject.Trim();
        return value.Length <= 50 ? value : value[..50].TrimEnd() + "…";
    }
}
