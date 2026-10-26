namespace Nekomata.Core.Missions;

public sealed record MissionWorkSegment(DateTime Start, DateTime End);

public static class MissionWorkSegmentBuilder
{
    public static IReadOnlyList<MissionWorkSegment> Build(
        IEnumerable<MissionWorkSegment> recorded,
        DateTime? activeStart,
        DateTime now,
        DateTime fallbackStart,
        TimeSpan fallbackElapsed)
    {
        var segments = recorded
            .Where(item => item.End > item.Start)
            .ToList();

        if (activeStart is DateTime start && now > start)
            segments.Add(new MissionWorkSegment(start, now));

        if (segments.Count == 0 && fallbackElapsed > TimeSpan.Zero)
            segments.Add(new MissionWorkSegment(fallbackStart, fallbackStart.Add(fallbackElapsed)));

        return segments
            .Where(item => item.End - item.Start >= TimeSpan.FromMinutes(1))
            .OrderBy(item => item.Start)
            .ToList();
    }
}
