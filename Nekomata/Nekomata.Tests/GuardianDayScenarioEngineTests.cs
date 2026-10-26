using Nekomata.Core.Guardian.Simulation;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianDayScenarioEngineTests
{
    [Theory]
    [InlineData(2026, 9, 12, 3, 2026, 9, 17)] // Saturday -> Mon, Tue, Wed -> Thursday exclusive
    [InlineData(2026, 9, 14, 3, 2026, 9, 17)] // Monday -> Mon, Tue, Wed -> Thursday exclusive
    [InlineData(2026, 9, 18, 3, 2026, 9, 23)] // Friday -> Fri, Mon, Tue -> Wednesday exclusive
    public void Calendar_query_covers_every_scheduled_working_day(
        int year, int month, int day, int horizon,
        int endYear, int endMonth, int endDay)
    {
        var end = GuardianDayScenarioEngine.WorkingHorizonEndExclusive(new DateTime(year, month, day), horizon);

        Assert.Equal(new DateTime(endYear, endMonth, endDay), end);
    }

    [Fact]
    public void Protect_plan_moves_selected_objective_ahead_of_current_ranking()
    {
        var scenarios = new GuardianDayScenarioEngine().Build(Input([
            Work(1, "Higher ranked", 1, 90),
            Work(2, "Chosen objective", 2, 60)
        ], preferred: 2));

        Assert.Equal(1, scenarios[0].Blocks[0].TaskId);
        Assert.Equal(2, scenarios[1].Blocks[0].TaskId);
        Assert.Contains(scenarios[1].DisplacedCommitments, x => x.Contains("Higher ranked"));
    }

    [Fact]
    public void Leave_plan_carries_work_into_the_next_working_day()
    {
        var input = Input([Work(1, "Long objective", 1, 300)]);
        input.LeaveTodayAt = new TimeSpan(10, 0, 0);

        var leave = new GuardianDayScenarioEngine().Build(input)[2];

        Assert.Contains(leave.Blocks, x => x.Start.Date > input.Now.Date);
        Assert.All(leave.Blocks.Where(x => x.Start.Date == input.Now.Date), x => Assert.True(x.End.TimeOfDay <= input.LeaveTodayAt));
    }

    [Fact]
    public void Fixed_commitments_are_never_overlapped()
    {
        var input = Input([Work(1, "Objective", 1, 180)]);
        input.Commitments = [new GuardianScenarioCommitment
        {
            Id = "meeting", Title = "Protected meeting",
            Start = At(input.Now.Date, 9, 0), End = At(input.Now.Date, 10, 0)
        }];

        var scenario = new GuardianDayScenarioEngine().Build(input)[0];

        Assert.DoesNotContain(scenario.Blocks, x => x.Start < input.Commitments[0].End && x.End > input.Commitments[0].Start);
    }

    [Fact]
    public void Existing_managed_commitment_on_a_later_horizon_day_is_never_overlapped()
    {
        var input = Input([Work(1, "Objective", 1, 1500)]);
        input.HorizonWorkingDays = 5;
        var laterDay = input.Now.Date.AddDays(2);
        input.Commitments = [new GuardianScenarioCommitment
        {
            Id = "existing-focus", Title = "Focus · Existing course",
            Start = At(laterDay, 15, 30), End = At(laterDay, 16, 0), IsManaged = true
        }];

        var scenario = new GuardianDayScenarioEngine().Build(input)[0];

        Assert.DoesNotContain(scenario.Blocks,
            block => block.Start < input.Commitments[0].End && block.End > input.Commitments[0].Start);
    }

    [Fact]
    public void Conflict_check_detects_calendar_change_after_a_scenario_was_built()
    {
        var input = Input([Work(1, "Objective", 1, 60)]);
        var scenario = new GuardianDayScenarioEngine().Build(input)[0];
        var changedCalendar = new GuardianScenarioCommitment
        {
            Id = "late-arrival",
            Title = "Pre-existing management course",
            Start = scenario.Blocks[0].Start.AddMinutes(15),
            End = scenario.Blocks[0].End.AddMinutes(15)
        };

        var conflict = GuardianDayScenarioEngine.FindFirstConflict(scenario, [changedCalendar]);

        Assert.Same(changedCalendar, conflict);
    }

    [Fact]
    public void Horizon_can_expand_from_three_to_five_working_days()
    {
        var input = Input([Work(1, "Very long objective", 1, 1800)]);
        var threeDay = new GuardianDayScenarioEngine().Build(input)[0];
        input.HorizonWorkingDays = 5;

        var fiveDay = new GuardianDayScenarioEngine().Build(input)[0];

        Assert.True(fiveDay.ScheduledMinutes > threeDay.ScheduledMinutes);
        Assert.True(fiveDay.UnscheduledMinutes < threeDay.UnscheduledMinutes);
        Assert.Contains(fiveDay.Evidence, x => x.Contains("5-working-day horizon"));
    }

    [Fact]
    public void Protecting_the_existing_first_item_explains_that_the_outcome_is_unchanged()
    {
        var scenarios = new GuardianDayScenarioEngine().Build(Input([
            Work(1, "Already first", 1, 60), Work(2, "Second", 2, 60)
        ], preferred: 1));

        Assert.False(scenarios[1].IsMateriallyDifferent);
        Assert.Contains("No material change", scenarios[1].ComparisonSummary);
    }

    private static GuardianDayScenarioInput Input(IReadOnlyList<GuardianScenarioWorkItem> work, long? preferred = null) => new()
    {
        Now = At(new DateTime(2026, 9, 9), 8, 0), WorkdayStart = new(8, 0, 0), WorkdayEnd = new(16, 0, 0),
        IncludeLunch = false, MinimumBlockMinutes = 30, CalendarLoaded = true,
        PreferredTaskId = preferred, LeaveTodayAt = new TimeSpan(16, 0, 0), WorkItems = work
    };

    private static GuardianScenarioWorkItem Work(long id, string title, int rank, int minutes) => new()
    {
        TaskId = id, Title = title, Rank = rank, Score = 100 - rank, Minutes = minutes
    };

    private static DateTimeOffset At(DateTime day, int hour, int minute) =>
        new(day.Date.AddHours(hour).AddMinutes(minute), TimeZoneInfo.Local.GetUtcOffset(day));
}
