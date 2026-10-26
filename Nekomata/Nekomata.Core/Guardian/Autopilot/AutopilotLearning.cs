namespace Nekomata.Core.Guardian.Autopilot;

public enum AutopilotLogKind { Moved, Undone, Nudged, NudgeFollowed }

/// <summary>One thing Autopilot did, or how the user responded. Times are local.</summary>
public sealed record AutopilotLogEntry(DateTime At, AutopilotLogKind Kind, string BlockId, DateTime? From = null, DateTime? To = null);

/// <summary>A stretch of focused work (a mission session).</summary>
public sealed record FocusSession(DateTime Start, int Minutes);

/// <summary>What Guardian has learned about how the user works, applied to planning and nudges.</summary>
public sealed class AutopilotProfile
{
    public List<int> AvoidHours { get; set; } = [];
    public int? PreferredStartHour { get; set; }
    public int? PreferredEndHour { get; set; }
    public int NudgeAfterMinutes { get; set; } = 10;
    public DateTime? LastRun { get; set; }
    public List<string> Notes { get; set; } = [];

    public string PreferredWindowLabel => PreferredStartHour is { } s && PreferredEndHour is { } e ? $"{s:00}:00 to {e:00}:00" : "";
}

/// <summary>
/// The weekly self-tuning pass. Deterministic and explainable: each change comes with a sentence saying why.
/// - Hours the user keeps undoing moves into (2+ undos, at least half the moves there) are avoided when placing blocks.
/// - The two-hour window where most of his focused work actually happens is protected for deep work.
/// - If he ignores most off-plan nudges they come later (20 minutes in); if he acts on most, back to 10.
/// </summary>
public static class AutopilotLearning
{
    public static AutopilotProfile Learn(
        AutopilotProfile previous, IReadOnlyCollection<AutopilotLogEntry> log, IReadOnlyCollection<FocusSession> sessions,
        DateTime now, int lookbackDays = 28)
    {
        var since = now.AddDays(-lookbackDays);
        var recent = log.Where(e => e.At >= since).ToList();
        var next = new AutopilotProfile { NudgeAfterMinutes = previous.NudgeAfterMinutes, LastRun = now };
        var notes = new List<string>();

        // Hours the user pushes back on.
        var movesByHour = recent.Where(e => e.Kind == AutopilotLogKind.Moved && e.To is not null)
            .GroupBy(e => e.To!.Value.Hour).ToDictionary(g => g.Key, g => g.Count());
        var undosByHour = recent.Where(e => e.Kind == AutopilotLogKind.Undone && e.To is not null)
            .GroupBy(e => e.To!.Value.Hour).ToDictionary(g => g.Key, g => g.Count());
        next.AvoidHours = undosByHour
            .Where(kv => kv.Value >= 2 && kv.Value * 2 >= movesByHour.GetValueOrDefault(kv.Key, kv.Value))
            .Select(kv => kv.Key).Order().ToList();
        foreach (var hour in next.AvoidHours.Except(previous.AvoidHours))
            notes.Add($"You've undone {undosByHour[hour]} moves into the {hour:00}:00 hour, so I'll stop putting focus blocks there.");
        foreach (var hour in previous.AvoidHours.Except(next.AvoidHours))
            notes.Add($"I'll start using the {hour:00}:00 hour for focus blocks again.");

        // Where focused work actually happens.
        var minutesByHour = new int[24];
        foreach (var session in sessions.Where(s => s.Start >= since && s.Minutes > 0))
        {
            var cursor = session.Start;
            var remaining = session.Minutes;
            while (remaining > 0)
            {
                var inThisHour = Math.Min(remaining, 60 - cursor.Minute);
                minutesByHour[cursor.Hour] += inThisHour;
                remaining -= inThisHour;
                cursor = cursor.AddMinutes(inThisHour);
            }
        }
        var total = minutesByHour.Sum();
        if (total >= 240)
        {
            var best = Enumerable.Range(6, 13).Select(h => (Hour: h, Minutes: minutesByHour[h] + minutesByHour[h + 1]))
                .OrderByDescending(x => x.Minutes).ThenBy(x => x.Hour).First();
            if (best.Minutes * 100 / total >= 25)
            {
                next.PreferredStartHour = best.Hour;
                next.PreferredEndHour = best.Hour + 2;
                if (previous.PreferredStartHour != best.Hour)
                    notes.Add($"Most of your focused work happens between {best.Hour:00}:00 and {best.Hour + 2:00}:00, " +
                              $"so I'll keep that window for your deepest work when I plan your day.");
            }
        }
        else
        {
            next.PreferredStartHour = previous.PreferredStartHour;
            next.PreferredEndHour = previous.PreferredEndHour;
        }

        // Nudges.
        var nudges = recent.Where(e => e.Kind == AutopilotLogKind.Nudged).ToList();
        if (nudges.Count >= 5)
        {
            var followed = nudges.Count(n => recent.Any(f => f.Kind == AutopilotLogKind.NudgeFollowed && f.BlockId == n.BlockId && f.At >= n.At));
            var ignoredRate = 100 * (nudges.Count - followed) / nudges.Count;
            if (ignoredRate >= 70 && previous.NudgeAfterMinutes < 20)
            {
                next.NudgeAfterMinutes = 20;
                notes.Add($"You've let {nudges.Count - followed} of my last {nudges.Count} drift nudges go, so I'll wait 20 minutes into a block before mentioning it.");
            }
            else if (ignoredRate <= 30 && previous.NudgeAfterMinutes > 10)
            {
                next.NudgeAfterMinutes = 10;
                notes.Add("You've been acting on my drift nudges, so I'll go back to mentioning it 10 minutes in.");
            }
        }

        next.Notes = notes;
        return next;
    }
}
