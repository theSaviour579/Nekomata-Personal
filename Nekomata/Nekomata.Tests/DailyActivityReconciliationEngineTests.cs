using Nekomata.Core.Guardian.Outcomes;
using Xunit;

namespace Nekomata.Tests;

public sealed class DailyActivityReconciliationEngineTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);
    // Evidence hours are clock times on the day (8 = 08:00), so build them from midnight, not from the 08:00 workday start.
    private static readonly DateTimeOffset Midnight = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private readonly DailyActivityReconciliationEngine _engine = new();

    [Fact]
    public void MatchingMissionAndCalendarDoNotDoubleCount()
    {
        var result = Run([
            Evidence("mission", "Mission", "Purchasing forecast analysis", 8, 9, ActivityEvidenceKind.Work),
            Evidence("calendar", "Calendar", "Focus · Purchasing forecast analysis", 8, 9, ActivityEvidenceKind.Calendar, true)
        ], Day.AddHours(2));

        Assert.Equal(60, result.RecordedWorkMinutes);
        Assert.Equal(1, result.MergedDuplicateCount);
        Assert.Equal(60, result.Gaps.Single().Minutes);
    }

    [Fact]
    public void CallInsideMissionIsAnInterruptionWithoutDoubleCountingElapsedDay()
    {
        var result = Run([
            Evidence("mission", "Mission", "Budget review", 8, 10, ActivityEvidenceKind.Work),
            Evidence("call", "Call", "Call with supplier", 8.5, 8.75, ActivityEvidenceKind.Interruption)
        ], Day.AddHours(2));

        Assert.Equal(120, result.ExplainedMinutes);
        Assert.Equal(120, result.RecordedWorkMinutes);
        Assert.Equal(15, result.InterruptionMinutes);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void LunchIsExcludedFromCoverageAndGaps()
    {
        var result = _engine.Reconcile(Day, Day.AddHours(8.5), Day.AddHours(6), Day.AddHours(4), Day.AddHours(5),
            [Evidence("morning", "Mission", "Morning work", 8, 12, ActivityEvidenceKind.Work),
             Evidence("afternoon", "Calendar", "Afternoon meeting", 13, 14, ActivityEvidenceKind.Calendar)]);

        Assert.Equal(300, result.WorkableMinutes);
        Assert.Equal(100, result.CoveragePercent);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void OnlyMaterialUnexplainedGapsAreSurfaced()
    {
        var result = Run([
            Evidence("a", "Mission", "One", 8, 8.5, ActivityEvidenceKind.Work),
            Evidence("b", "Calendar", "Two", 8.67, 9, ActivityEvidenceKind.Calendar),
            Evidence("c", "Mission", "Three", 9.5, 10, ActivityEvidenceKind.Work)
        ], Day.AddHours(2));

        Assert.Single(result.Gaps);
        Assert.Equal(30, result.Gaps[0].Minutes);
    }

    [Fact]
    public void ConfirmedNonWorkExplainsTimeButDoesNotBecomeRecordedWork()
    {
        var result = Run([
            Evidence("break", "User review", "Personal break", 8, 8.5, ActivityEvidenceKind.NonWork)
        ], Day.AddHours(1));

        Assert.Equal(30, result.ExplainedMinutes);
        Assert.Equal(0, result.RecordedWorkMinutes);
        Assert.Equal(30, result.Gaps.Single().Minutes);
    }

    private DailyActivityReconciliationResult Run(IEnumerable<ActivityEvidence> items, DateTimeOffset asOf) =>
        _engine.Reconcile(Day, Day.AddHours(8.5), asOf, null, null, items);

    private static ActivityEvidence Evidence(string key, string source, string title, double fromHour, double toHour,
        ActivityEvidenceKind kind, bool managed = false) =>
        new(key, source, title, Midnight.AddHours(fromHour), Midnight.AddHours(toHour), kind, 100, managed);
}
