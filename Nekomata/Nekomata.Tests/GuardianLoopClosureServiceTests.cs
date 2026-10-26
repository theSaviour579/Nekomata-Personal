using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianLoopClosureServiceTests
{
    [Fact]
    public async Task Cancels_unsent_draft_resolves_loop_and_keeps_provenance()
    {
        var loop = new GuardianOpenLoop { Id = Guid.NewGuid(), Title = "Purchasing DB Handover", WaitingOn = "Phil Mathers" };
        var attempt = new GuardianChaseAttempt { Id = Guid.NewGuid(), OpenLoopId = loop.Id, Recipient = "Phil Mathers", Status = GuardianChaseStatus.Approved };
        var chases = new FakeChases(attempt);
        var loops = new FakeLoops(loop);
        var events = new FakeEvents();
        var service = new GuardianLoopClosureService(chases, loops, new WorkdayJournalService(events));

        var result = await service.CancelDraftAndResolveAsync("Phil", DateTimeOffset.UtcNow);

        Assert.True(result.Success);
        Assert.Equal(GuardianChaseStatus.Cancelled, attempt.Status);
        Assert.Equal(OpenLoopStatus.Resolved, loop.Status);
        Assert.Contains(events.Items, x => x.EventType == "OpenLoopDraftCancelledAndResolved");
    }

    [Fact]
    public async Task Requires_selection_when_query_matches_multiple_loops()
    {
        var first = new GuardianOpenLoop { Id = Guid.NewGuid(), Title = "Purchasing one", WaitingOn = "Phil" };
        var second = new GuardianOpenLoop { Id = Guid.NewGuid(), Title = "Purchasing two", WaitingOn = "IT" };
        var service = new GuardianLoopClosureService(
            new FakeChases(
                new GuardianChaseAttempt { Id = Guid.NewGuid(), OpenLoopId = first.Id, Recipient = "Phil" },
                new GuardianChaseAttempt { Id = Guid.NewGuid(), OpenLoopId = second.Id, Recipient = "IT" }),
            new FakeLoops(first, second), new WorkdayJournalService(new FakeEvents()));

        var result = await service.CancelDraftAndResolveAsync("Purchasing", DateTimeOffset.UtcNow);

        Assert.False(result.Success);
        Assert.True(result.NeedsSelection);
        Assert.Equal(OpenLoopStatus.Open, first.Status);
        Assert.Equal(OpenLoopStatus.Open, second.Status);
    }

    private sealed class FakeChases(params GuardianChaseAttempt[] attempts) : IGuardianChaseRepository
    {
        public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid id) => Task.FromResult(attempts.FirstOrDefault(x => x.OpenLoopId == id && x.Status == GuardianChaseStatus.Prepared));
        public Task<int> GetAttemptCountAsync(Guid id) => Task.FromResult(attempts.Count(x => x.OpenLoopId == id));
        public Task AddAsync(GuardianChaseAttempt value) => Task.CompletedTask;
        public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) => Task.FromResult(attempts.Where(x => x.Status == status).ToList());
        public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid id) => Task.FromResult(attempts.Where(x => x.OpenLoopId == id).ToList());
        public Task<bool> ApproveAsync(Guid id, DateTimeOffset at) => Task.FromResult(false);
        public Task RecordSendFailureAsync(Guid id, DateTimeOffset at, string error) => Task.CompletedTask;
        public Task<bool> MarkSentAsync(Guid id, DateTimeOffset sentAt, DateTimeOffset due) => Task.FromResult(false);
        public Task<bool> CancelAsync(Guid id, DateTimeOffset at)
        {
            var item = attempts.Single(x => x.Id == id);
            if (item.Status is not GuardianChaseStatus.Prepared and not GuardianChaseStatus.Approved) return Task.FromResult(false);
            item.Status = GuardianChaseStatus.Cancelled;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop value) => Task.FromResult(value);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.Where(x => x.Status == OpenLoopStatus.Open).ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => GetOpenAsync(count);
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence)
        {
            var item = loops.Single(x => x.Id == id);
            item.Status = OpenLoopStatus.Resolved;
            item.ResolvedAt = evidence.OccurredAt;
            item.ResolutionSummary = evidence.Summary;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent value) { Items.Add(value); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(new List<WorkdayEvent>());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(new List<WorkdayEvent>());
        public Task<bool> ExistsAsync(string eventType, string externalId) => Task.FromResult(false);
    }
}
