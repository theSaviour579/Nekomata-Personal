using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Tasks;
using Xunit;

namespace Nekomata.Tests;

public class WrapUpPlanningTests
{
    private static readonly DateTime Friday = new(2026, 9, 4);
    [Fact] public void Friday_continues_on_Monday() => Assert.Equal(new DateTime(2026, 9, 7), WrapUpPlanning.NextWorkday(Friday));
    [Fact] public void Deferred_work_stays_hidden_until_date()
    {
        var item = new WrapUpObjective { Decision = "Defer until", DeferUntil = Friday.AddDays(10) };
        Assert.False(WrapUpPlanning.IsDue(item, Friday));
        Assert.True(WrapUpPlanning.IsDue(item, Friday.AddDays(10)));
    }
    [Theory]
    [InlineData("No change")]
    [InlineData("Remove from next plan")]
    public void Non_carry_choices_do_not_enter_preflight(string choice) => Assert.False(WrapUpPlanning.IsDue(new() { Decision = choice }, Friday));
    [Fact] public void Only_unfinished_due_or_scheduled_tasks_are_offered()
    {
        NekomataTask[] tasks = [new() { Id = 1, DueAt = Friday }, new() { Id = 2, DueAt = Friday, Completed = true }, new() { Id = 3, DueAt = Friday, Status = "Completed" }, new() { Id = 4 }, new() { Id = 5, IsScheduled = true, StartAt = Friday }];
        Assert.Equal(new long?[] { 1, 5 }, WrapUpPlanning.Build(tasks, Friday).Select(x => x.TaskId));
    }
}
