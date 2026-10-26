using Nekomata.Core.Guardian.Autopilot;
using Xunit;

namespace Nekomata.Tests;

public sealed class DayAutopilotPlannerTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
    private static DateTimeOffset At(int h, int m = 0) => new(2026, 10, 14, h, m, 0, Offset);
    private static AutopilotEvent Meeting(string id, int sh, int sm, int eh, int em) => new(id, id, At(sh, sm), At(eh, em), false, false);
    private static AutopilotEvent Block(string id, int sh, int sm, int eh, int em) => new(id, id, At(sh, sm), At(eh, em), false, true);
    private static AutopilotPlan Plan(IEnumerable<AutopilotEvent> events, DateTimeOffset now,
        IReadOnlySet<string>? missed = null, IReadOnlySet<string>? push = null, IReadOnlySet<string>? handsOff = null) =>
        DayAutopilotPlanner.Replan(events, now, At(9), At(17), missed, push, handsOff);

    [Fact]
    public void NoClash_NoMoves() =>
        Assert.True(Plan([Meeting("ops", 10, 0, 11, 0), Block("intune", 11, 30, 12, 30)], At(9, 30)).IsEmpty);

    [Fact]
    public void ClashingBlock_MovesToNearestFreeGapAfterMeetingBuffer()
    {
        var plan = Plan([Meeting("ops", 14, 0, 15, 0), Block("intune", 14, 0, 15, 0)], At(10));
        var move = Assert.Single(plan.Moves);
        Assert.Equal("intune", move.EventId);
        Assert.Equal(At(13, 0), move.NewStart); // an hour fits before the meeting; nearer than 15:05
        Assert.Equal(At(14, 0), move.NewEnd);
        Assert.Contains("ops", move.Reason);
    }

    [Fact]
    public void ClashingBlock_GoesAfterMeetingWhenMorningIsFull()
    {
        var plan = Plan([Meeting("all-morning", 9, 0, 14, 0), Meeting("ops", 14, 0, 15, 0), Block("intune", 14, 0, 15, 0)], At(9, 30));
        var move = Assert.Single(plan.Moves);
        Assert.Equal(At(15, 5), move.NewStart);
        Assert.Equal(At(16, 5), move.NewEnd);
    }

    [Fact]
    public void NeverPlacesBeforeNow()
    {
        var plan = Plan([Meeting("call", 13, 0, 14, 0), Block("pricing", 13, 0, 14, 0)], At(12, 50));
        var move = Assert.Single(plan.Moves);
        Assert.True(move.NewStart >= At(12, 50));
        Assert.Equal(At(14, 5), move.NewStart);
    }

    [Fact]
    public void RunningBlockIsNotMovedForAClash() =>
        Assert.True(Plan([Block("intune", 10, 0, 11, 0), Meeting("adhoc", 10, 30, 11, 0)], At(10, 15)).IsEmpty);

    [Fact]
    public void MissedBlock_IsRescheduledLaterToday()
    {
        var plan = Plan([Block("intune", 9, 0, 10, 0), Meeting("ops", 11, 0, 12, 0)], At(10, 2), missed: new HashSet<string> { "intune" });
        var move = Assert.Single(plan.Moves);
        // 10:05-11:00 is too short for the full hour, so it goes after ops rather than being cut down.
        Assert.Equal(At(12, 5), move.NewStart);
        Assert.Equal(At(13, 5), move.NewEnd);
    }

    [Fact]
    public void PastBlockThatWasNotMissed_IsLeftAlone() =>
        Assert.True(Plan([Block("intune", 9, 0, 10, 0)], At(10, 30)).IsEmpty);

    [Fact]
    public void PushedRunningBlock_GoesAfterItsCurrentSlot()
    {
        var plan = Plan([Block("intune", 10, 0, 11, 0)], At(10, 10), push: new HashSet<string> { "intune" });
        var move = Assert.Single(plan.Moves);
        Assert.True(move.NewStart >= At(11, 0));
    }

    [Fact]
    public void ShortensRatherThanDropsWhenOnlyASmallerGapIsLeft()
    {
        var plan = Plan([Meeting("a", 9, 0, 15, 0), Meeting("b", 15, 40, 17, 0), Block("big", 15, 0, 16, 30)], At(9, 5));
        var move = Assert.Single(plan.Moves);
        Assert.True(move.Shortened);
        Assert.Equal(At(15, 5), move.NewStart);
        Assert.Equal(At(15, 40), move.NewEnd);
    }

    [Fact]
    public void ReportsBlocksThatCannotFit()
    {
        var plan = Plan([Meeting("a", 9, 0, 17, 0), Block("intune", 14, 0, 15, 0)], At(9, 5));
        Assert.Empty(plan.Moves);
        Assert.Equal("intune", Assert.Single(plan.Unplaced).EventId);
    }

    [Fact]
    public void HandsOffBlocksAreNeverMoved() =>
        Assert.True(Plan([Meeting("ops", 14, 0, 15, 0), Block("intune", 14, 0, 15, 0)], At(10), handsOff: new HashSet<string> { "intune" }).IsEmpty);

    [Fact]
    public void TwoClashingBlocks_DoNotOverlapEachOther()
    {
        var plan = Plan([Meeting("all", 9, 0, 14, 0), Meeting("x", 14, 0, 15, 0), Block("one", 14, 0, 15, 0), Block("two", 14, 0, 15, 0)], At(9, 5));
        Assert.Equal(2, plan.Moves.Count);
        var (a, b) = (plan.Moves[0], plan.Moves[1]);
        Assert.True(a.NewEnd <= b.NewStart || b.NewEnd <= a.NewStart);
    }

    [Fact]
    public void FindSlot_ReturnsEarliestGapAfterMeetingBuffer()
    {
        var slot = DayAutopilotPlanner.FindSlot([Meeting("ops", 10, 0, 11, 0), Block("intune", 11, 30, 12, 30)],
            At(10, 20), At(17), At(9), At(17), TimeSpan.FromMinutes(30));
        Assert.Equal(At(12, 30), slot!.Value.Start); // 11:05-11:30 is too short
    }

    [Fact]
    public void FindSlot_RespectsDeadline() =>
        Assert.Null(DayAutopilotPlanner.FindSlot([Meeting("all", 9, 0, 16, 0)], At(9), At(15), At(9), At(17), TimeSpan.FromMinutes(30)));
}
