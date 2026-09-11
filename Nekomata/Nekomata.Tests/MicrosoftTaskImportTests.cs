using Nekomata.Core.Integrations;
using Nekomata.Integrations.MicrosoftGraph.Tasks;
using Xunit;

namespace Nekomata.Tests;

public sealed class MicrosoftTaskImportTests
{
    [Fact]
    public void Summary_IncludesImportedDueAndOverdueTasksWithoutDuplicatingRanking()
    {
        var workspace = new Nekomata.Models.Workspace.NekomataWorkspace();
        workspace.IntegrationMissionCandidates.AddRange([
            new() { SourceType = "Microsoft Planner", Title = "Due today", DueAt = DateTime.Today },
            new() { SourceType = "Microsoft Planner", Title = "Overdue", DueAt = DateTime.Today.AddDays(-2) },
            new() { SourceType = "Microsoft To Do", Title = "To Do", DueAt = DateTime.Today },
            new() { SourceType = "Halo", Title = "Ticket", DueAt = DateTime.Today }
        ]);

        var tasks = Nekomata.Core.Workspace.WorkspaceTaskSummary.GetTasks(workspace).ToList();

        Assert.Equal(3, tasks.Count);
        Assert.Equal(2, tasks.Count(task => task.DueAt == DateTime.Today));
        Assert.Single(tasks, task => task.DueAt < DateTime.Today);
        Assert.Empty(workspace.Tasks);
        workspace.IntegrationMissionCandidates.Clear();
        Assert.Empty(Nekomata.Core.Workspace.WorkspaceTaskSummary.GetTasks(workspace));
    }

    [Fact]
    public async Task Import_FollowsPlannerPagesAndExcludesCompletedItems()
    {
        using var http = new HttpClient(new PagedHandler())
        { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };
        var service = new MicrosoftTaskService(http, new TestAuthentication());
        var snapshot = await service.GetOpenTasksAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["First", "Second"], snapshot.PlannerTasks.Select(task => task.Title));
    }

    private sealed class PagedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("todo/lists") ? "{\"value\":[]}" :
                request.RequestUri.Query.Length == 0
                    ? """{"value":[{"id":"1","title":"First"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/planner/tasks?page=2"}"""
                    : """{"value":[{"id":"2","title":"Second"},{"id":"3","title":"Done","percentComplete":100}]}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class TestAuthentication : Nekomata.Integrations.MicrosoftGraph.Authentication.IMicrosoftAuthenticationService
    {
        public Task<string?> GetConnectedAccountAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test@example.com");
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Nekomata.Integrations.MicrosoftGraph.Authentication.TokenResult> GetTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new Nekomata.Integrations.MicrosoftGraph.Authentication.TokenResult { AccessToken = "test" });
    }

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
