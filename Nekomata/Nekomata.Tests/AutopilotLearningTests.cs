using Nekomata.Core.Guardian.Autopilot;
using Xunit;

namespace Nekomata.Tests;

public sealed class AutopilotLearningTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 8, 0, 0);
    private static DateTime Day(int daysAgo, int hour, int minute = 0) => Now.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute);

    [Fact]
    public void RepeatedlyUndoneHour_IsAvoided()
    {
        var log = new List<AutopilotLogEntry>
        {
            new(Day(3, 12), AutopilotLogKind.Moved, "a", Day(3, 11), Day(3, 16)),
            new(Day(3, 12, 5), AutopilotLogKind.Undone, "a", Day(3, 11), Day(3, 16)),
            new(Day(5, 12), AutopilotLogKind.Moved, "b", Day(5, 10), Day(5, 16, 30)),
            new(Day(5, 12, 5), AutopilotLogKind.Undone, "b", Day(5, 10), Day(5, 16, 30)),
            new(Day(6, 12), AutopilotLogKind.Moved, "c", Day(6, 10), Day(6, 14))
        };
        var profile = AutopilotLearning.Learn(new AutopilotProfile(), log, [], Now);
        Assert.Equal(new[] { 16 }, profile.AvoidHours);
        Assert.Contains(profile.Notes, n => n.Contains("16:00"));
    }

    [Fact]
    public void SingleUndo_IsNotAPattern() =>
        Assert.Empty(AutopilotLearning.Learn(new AutopilotProfile(), [
            new(Day(3, 12), AutopilotLogKind.Moved, "a", Day(3, 11), Day(3, 16)),
            new(Day(3, 12, 5), AutopilotLogKind.Undone, "a", Day(3, 11), Day(3, 16))], [], Now).AvoidHours);

    [Fact]
    public void FocusWindow_IsWhereMostFocusedWorkHappens()
    {
        var sessions = Enumerable.Range(1, 10).Select(d => new FocusSession(Day(d, 9), 90))
            .Append(new FocusSession(Day(2, 15), 30)).ToList();
        var profile = AutopilotLearning.Learn(new AutopilotProfile(), [], sessions, Now);
        Assert.Equal(9, profile.PreferredStartHour);
        Assert.Equal(11, profile.PreferredEndHour);
        Assert.Contains(profile.Notes, n => n.Contains("09:00 and 11:00"));
    }

    [Fact]
    public void TooLittleFocusData_KeepsPreviousWindow()
    {
        var previous = new AutopilotProfile { PreferredStartHour = 10, PreferredEndHour = 12 };
        var profile = AutopilotLearning.Learn(previous, [], [new FocusSession(Day(1, 14), 60)], Now);
        Assert.Equal(10, profile.PreferredStartHour);
        Assert.Empty(profile.Notes);
    }

    [Fact]
    public void MostlyIgnoredNudges_ComeLater()
    {
        var log = Enumerable.Range(1, 6).Select(d => new AutopilotLogEntry(Day(d, 10, 10), AutopilotLogKind.Nudged, $"b{d}")).ToList();
        log.Add(new(Day(1, 10, 12), AutopilotLogKind.NudgeFollowed, "b1"));
        var profile = AutopilotLearning.Learn(new AutopilotProfile(), log, [], Now);
        Assert.Equal(20, profile.NudgeAfterMinutes);
    }

    [Fact]
    public void FollowedNudges_GoBackToTenMinutes()
    {
        var log = new List<AutopilotLogEntry>();
        for (var d = 1; d <= 6; d++)
        {
            log.Add(new(Day(d, 10, 20), AutopilotLogKind.Nudged, $"b{d}"));
            log.Add(new(Day(d, 10, 22), AutopilotLogKind.NudgeFollowed, $"b{d}"));
        }
        Assert.Equal(10, AutopilotLearning.Learn(new AutopilotProfile { NudgeAfterMinutes = 20 }, log, [], Now).NudgeAfterMinutes);
    }

    [Fact]
    public void PlannerAvoidsLearnedHoursWhenThereIsAChoice()
    {
        static DateTimeOffset At(int h, int m = 0) => new(2026, 10, 19, h, m, 0, TimeSpan.FromHours(1));
        var events = new[]
        {
            new AutopilotEvent("ops", "ops", At(14), At(15), false, false),
            new AutopilotEvent("intune", "intune", At(14), At(15), false, true),
            new AutopilotEvent("lunch", "lunch", At(12), At(13), false, false)
        };
        // Without learning it would go to 15:05 (nearest to 14:00); Alex keeps undoing moves into the 15:00 hour.
        Assert.Equal(15, Assert.Single(DayAutopilotPlanner.Replan(events, At(10), At(9), At(17)).Moves).NewStart.Hour);
        var options = new AutopilotOptions { AvoidHours = new HashSet<int> { 15 } };
        var move = Assert.Single(DayAutopilotPlanner.Replan(events, At(10), At(9), At(17), options: options).Moves);
        Assert.Equal(At(11), move.NewStart);
    }
}
