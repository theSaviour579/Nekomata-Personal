using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class AnticipatedActionWorkflowServiceTests
{
    [Fact]
    public async Task Approves_without_sending_then_marks_sent_only_after_graph_confirms()
    {
        var action = Prepared();
        var workspace = new NekomataWorkspace { AnticipatedActions = [action] };
        var graph = new FakeGraph();
        var events = new FakeEvents();
        var service = new AnticipatedActionWorkflowService(graph, new WorkdayJournalService(events));
        var now = DateTimeOffset.UtcNow;

        Assert.Single(await service.ApprovePreparedAsync(workspace, now));
        Assert.Equal(AnticipatedActionStatus.Approved, action.Status);
        Assert.Empty(graph.Sent);

        var result = Assert.Single(await service.SendApprovedAsync(workspace, now.AddMinutes(1)));
        Assert.True(result.Sent);
        Assert.Equal(AnticipatedActionStatus.Sent, action.Status);
        Assert.Single(graph.Sent);
        Assert.Contains(events.Items, x => x.EventType == "AnticipatedActionApproved");
        Assert.Contains(events.Items, x => x.EventType == "AnticipatedActionSent");
    }

    [Fact]
    public async Task Failed_graph_send_remains_approved_and_records_error()
    {
        var action = Prepared();
        action.Status = AnticipatedActionStatus.Approved;
        var workspace = new NekomataWorkspace { AnticipatedActions = [action] };
        var graph = new FakeGraph { Failure = new InvalidOperationException("permission denied") };
        var events = new FakeEvents();
        var service = new AnticipatedActionWorkflowService(graph, new WorkdayJournalService(events));

        var result = Assert.Single(await service.SendApprovedAsync(workspace, DateTimeOffset.UtcNow));

        Assert.False(result.Sent);
        Assert.Equal(AnticipatedActionStatus.Approved, action.Status);
        Assert.Contains("permission denied", action.LastSendError);
        Assert.Contains(events.Items, x => x.EventType == "AnticipatedActionSendFailed" && x.Outcome == "Approved");
    }

    private static AnticipatedAction Prepared() => new()
    {
        Id = Guid.NewGuid(), SourceEventId = Guid.NewGuid(), ProposedAction = "PrepareMeetingReply",
        PreparedContent = "Hi Craig, does 09:30 work?", TargetConversationId = "chat-1",
        Status = AnticipatedActionStatus.Prepared, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
    };

    private sealed class FakeGraph : IGraphContextService
    {
        public Exception? Failure { get; init; }
        public List<(string ChatId, string Content)> Sent { get; } = [];
        public Task SendChatMessageAsync(string chatId, string content, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Sent.Add((chatId, content));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<GraphChatMessage>> GetRecentChatMessagesAsync(int maximumChats = 20, int messagesPerChat = 10, int lookbackDays = 7, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GraphChatMessage>>([]);
        public Task<IReadOnlyList<GraphTodoTask>> GetOpenTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GraphTodoTask>>([]);
        public Task<IReadOnlyList<GraphPlannerTask>> GetPlannerTasksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GraphPlannerTask>>([]);
        public Task<GraphPresence> GetPresenceAsync(CancellationToken cancellationToken = default) => Task.FromResult(new GraphPresence());
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent value) { Items.Add(value); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.Where(x => x.OccurredAt >= from && x.OccurredAt < to).ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(new List<WorkdayEvent>());
        public Task<bool> ExistsAsync(string eventType, string externalId) => Task.FromResult(Items.Any(x => x.EventType == eventType && x.ExternalId == externalId));
    }
}
