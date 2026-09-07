using Nekomata.Core.Integrations;
using Nekomata.Integrations.MicrosoftGraph.Tasks;
using Xunit;

namespace Nekomata.Tests;

public sealed class MicrosoftTaskImportTests
{
    [Fact]
    public void DeduplicationKey_IgnoresCaseAndRepeatedWhitespace()
    {
        var due = new DateTime(2026, 9, 8, 9, 30, 0);
        var todo = Item("Microsoft To Do", "  Review   proposal ", due);
        var planner = Item("Microsoft Planner", "review proposal", due.AddHours(3));

        Assert.Equal(
            MicrosoftTasksWorkspaceDataSource.DeduplicationKey(todo),
            MicrosoftTasksWorkspaceDataSource.DeduplicationKey(planner));
    }

    [Fact]
    public void DeduplicationKey_PreservesDifferentDueDates()
    {
        var first = Item("Microsoft To Do", "Review proposal", new DateTime(2026, 9, 8));
        var second = Item("Microsoft Planner", "Review proposal", new DateTime(2026, 9, 9));

        Assert.NotEqual(
            MicrosoftTasksWorkspaceDataSource.DeduplicationKey(first),
            MicrosoftTasksWorkspaceDataSource.DeduplicationKey(second));
    }

    private static MicrosoftTaskItem Item(string source, string title, DateTime due) =>
        new(source, Guid.NewGuid().ToString(), title, "", "Not started", "Normal", due, null, null, "");
}
