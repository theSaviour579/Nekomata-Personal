using Nekomata.Core.Guardian.Simulation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianDayScenarioEngineTests
{
    [Fact]
    public void Protect_plan_moves_selected_objective_ahead_of_current_ranking()
    {
        var scenarios = new GuardianDayScenarioEngine().Build(Input([Work(1, "Higher ranked", 1, 90), Work(2, "Chosen objective", 2, 60)], 2));
        Assert.Equal(1, scenarios[0].Blocks[0].TaskId);
        Assert.Equal(2, scenarios[1].Blocks[0].TaskId);
        Assert.Contains(scenarios[1].DisplacedCommitments, x => x.Contains("Higher ranked"));
    }

    [Fact]
    public void Leave_plan_carries_work_into_the_next_working_day()
    {
        var input = Input([Work(1, "Long objective", 1, 300)]);
        input.LeaveTodayAt = new(10, 0, 0);
        var leave = new GuardianDayScenarioEngine().Build(input)[2];
        Assert.Contains(leave.Blocks, x => x.Start.Date > input.Now.Date);
        Assert.All(leave.Blocks.Where(x => x.Start.Date == input.Now.Date), x => Assert.True(x.End.TimeOfDay <= input.LeaveTodayAt));
    }

    [Fact]
    public void Fixed_commitments_are_never_overlapped()
    {
        var input = Input([Work(1, "Objective", 1, 180)]);
        input.Commitments = [new() { Id = "meeting", Title = "Protected meeting", Start = At(input.Now.Date, 9), End = At(input.Now.Date, 10) }];
        var scenario = new GuardianDayScenarioEngine().Build(input)[0];
        Assert.DoesNotContain(scenario.Blocks, x => x.Start < input.Commitments[0].End && x.End > input.Commitments[0].Start);
    }

    private static GuardianDayScenarioInput Input(IReadOnlyList<GuardianScenarioWorkItem> work, long? preferred = null) => new()
    {
        Now = At(new(2026, 9, 9), 8), WorkdayStart = new(8, 0, 0), WorkdayEnd = new(16, 0, 0), IncludeLunch = false,
        MinimumBlockMinutes = 30, CalendarLoaded = true, PreferredTaskId = preferred, LeaveTodayAt = new(16, 0, 0), WorkItems = work
    };
    private static GuardianScenarioWorkItem Work(long id, string title, int rank, int minutes) => new() { TaskId = id, Title = title, Rank = rank, Score = 100 - rank, Minutes = minutes };
    private static DateTimeOffset At(DateTime day, int hour) => new(day.Date.AddHours(hour), TimeZoneInfo.Local.GetUtcOffset(day));
}
