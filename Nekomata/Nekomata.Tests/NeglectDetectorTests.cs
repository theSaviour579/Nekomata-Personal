using Nekomata.Core.Guardian.Neglect;
using Xunit;

namespace Nekomata.Tests;

public sealed class NeglectDetectorTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 30, 0);

    private static IReadOnlyList<NeglectItem> Detect(
        NeglectTask[]? tasks = null, NeglectProject[]? projects = null, NeglectPromise[]? promises = null, NeglectReply[]? replies = null) =>
        NeglectDetector.Detect(Now, tasks ?? [], projects ?? [], promises ?? [], replies ?? []);

    [Fact]
    public void RolledForwardTask_IsSlipping()
    {
        var item = Assert.Single(Detect([new(1, "IT spend baseline", null, 10, false, 4, Now.AddDays(-1))]));
        Assert.Equal(NeglectKind.SlippingTask, item.Kind);
        Assert.Equal("Rolled forward 4 times.", item.Why);
    }

    [Fact]
    public void OverdueTask_IsSlipping() =>
        Assert.Contains("5 days overdue", Assert.Single(Detect([new(1, "Renewals", Now.AddDays(-5), 10, false, 0, Now)])).Why);

    [Fact]
    public void RecentlyWorkedOrOnTimeTasks_AreFine() =>
        Assert.Empty(Detect([new(1, "A", Now.AddDays(2), 90, false, 1, Now.AddDays(-1)), new(2, "B", null, 10, false, 0, Now)]));

    [Fact]
    public void HighValueUntouchedTask_IsAvoided()
    {
        var tasks = new NeglectTask[]
        {
            new(1, "Layout pilot", null, 95, false, 0, Now.AddDays(-9)),
            new(2, "Low 1", null, 10, false, 0, null), new(3, "Low 2", null, 12, false, 0, null),
            new(4, "Low 3", null, 14, false, 0, null), new(5, "Low 4", null, 16, false, 0, null)
        };
        var item = Assert.Single(Detect(tasks));
        Assert.Equal(NeglectKind.AvoidedWork, item.Kind);
        Assert.Contains("9 days", item.Why);
    }

    [Fact]
    public void ScheduledHighValueTask_IsNotAvoided() =>
        Assert.Empty(Detect([new(1, "Layout pilot", null, 95, true, 0, null)]));

    [Fact]
    public void PromiseDueSoonWithoutTime_ComesFirst()
    {
        var items = Detect(
            tasks: [new(1, "Renewals", Now.AddDays(-3), 10, false, 0, Now)],
            promises: [new("Send Sam the PP data", "Sam", new DateTimeOffset(Now.AddHours(20)), false)]);
        Assert.Equal(NeglectKind.UnbookedPromise, items[0].Kind);
        Assert.Contains("tomorrow", items[0].Why);
    }

    [Fact]
    public void BookedOrDistantPromises_AreIgnored() =>
        Assert.Empty(Detect(promises: [new("A", "Sam", new DateTimeOffset(Now.AddHours(5)), true), new("B", "Tom", new DateTimeOffset(Now.AddDays(5)), false)]));

    [Fact]
    public void ActiveProjectIdleForTwoWeeks_IsStale()
    {
        var item = Assert.Single(Detect(projects: [new(1, "Category review", "Active", Now.AddDays(-20), null, true, null)]));
        Assert.Equal(NeglectKind.StaleProject, item.Kind);
        Assert.Contains("20 days", item.Why);
        Assert.Contains("at risk", item.Why);
    }

    [Fact]
    public void SeveralStalledProjects_CollapseIntoOneLine()
    {
        var projects = Enumerable.Range(1, 8).Select(i => new NeglectProject(i, $"Project {i}", "Active", Now.AddDays(-92), null, false, null)).ToArray();
        var item = Assert.Single(Detect(projects: projects));
        Assert.Equal(NeglectKind.StaleProject, item.Kind);
        Assert.Equal("8 active projects", item.Title);
        Assert.Equal(8, item.ProjectIds.Count);
        Assert.Contains("and 5 more", item.Why);
        Assert.Contains("13+ weeks", item.Why);
    }

    [Fact]
    public void PromiseOverdueByMoreThanAWeek_IsLeftOut() =>
        Assert.Empty(Detect(promises: [new("I'll deal with it tomorrow", "", new DateTimeOffset(Now.AddDays(-9)), false)]));

    [Fact]
    public void OverduePromise_KeepsThePromiseForBooking()
    {
        var item = Assert.Single(Detect(promises: [new("I'll send the data tomorrow", "Sam", new DateTimeOffset(Now.AddDays(-1)), false, "email")]));
        Assert.NotNull(item.Promise);
        Assert.Contains("to Sam (email)", item.Why);
        Assert.Contains("yesterday", item.Why);
    }

    [Fact]
    public void RecentlyWorkedProject_IsNotStale() =>
        Assert.Empty(Detect(projects: [new(1, "Category review", "Active", Now.AddDays(-30), Now.AddDays(-2), false, null)]));

    [Fact]
    public void RepliesOwed_OnlyBetweenTwoAndFourteenDays() =>
        Assert.Single(Detect(replies:
        [
            new("Sam", "Data", new DateTimeOffset(Now.AddDays(-1))),
            new("Tom", "Licence", new DateTimeOffset(Now.AddDays(-4))),
            new("Old", "Ancient", new DateTimeOffset(Now.AddDays(-30)))
        ]));
}
