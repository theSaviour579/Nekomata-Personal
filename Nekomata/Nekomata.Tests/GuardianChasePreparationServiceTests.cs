using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianChasePreparationServiceTests
{
    [Fact]
    public async Task Prepares_only_mature_loops_and_reuses_pending_attempt()
    {
        var mature = Loop("Phil Mathers", 3, 42);
        var recent = Loop("Emma Smith", 1, null);
        var loops = new FakeLoops(mature, recent);
        var events = new FakeEvents();
        var chases = new FakeChases();
        var age = new GuardianOpenLoopAgeService(loops, new WorkdayJournalService(events));
        var service = new GuardianChasePreparationService(age, chases, new WorkdayJournalService(events));
        var workspace = new NekomataWorkspace { Projects = [new NekomataProject { Id = 42, Name = "Purchasing Review" }] };
        var now = DateTimeOffset.Parse("2026-08-20T10:00:00+01:00");

        var first = await service.PrepareMatureAsync(workspace, now);
        var second = await service.PrepareMatureAsync(workspace, now.AddMinutes(5));

        var attempt = Assert.Single(first);
        Assert.Same(attempt, Assert.Single(second));
        Assert.Equal("Phil Mathers", attempt.Recipient);
        Assert.Equal("Purchasing Review", attempt.ContextLabel);
        Assert.Contains("Hi Phil,", attempt.Body);
        Assert.Equal(GuardianChaseStatus.Prepared, attempt.Status);
        Assert.Null(attempt.ApprovedAt);
        Assert.Null(attempt.SentAt);
        Assert.Single(chases.Items);
    }

    private static GuardianOpenLoop Loop(string person, int workingDaysOld, long? projectId)
    {
        var now = new DateTime(2026, 8, 20);
        var created = now;
        while (workingDaysOld > 0)
        {
            created = created.AddDays(-1);
            if (created.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) workingDaysOld--;
        }
        return new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Type = OpenLoopType.AwaitingThem, Status = OpenLoopStatus.Open,
            CreatedAt = new DateTimeOffset(created.AddHours(9), TimeSpan.FromHours(1)), WaitingOn = person,
            Title = "Awaiting reply: Purchasing DB Handover", ProjectId = projectId,
            SourceEventId = Guid.NewGuid()
        };
    }

    private sealed class FakeLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop value) => Task.FromResult(value);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeChases : IGuardianChaseRepository
    {
        public List<GuardianChaseAttempt> Items { get; } = [];
        public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(x => x.OpenLoopId == id && x.Status == GuardianChaseStatus.Prepared));
        public Task<int> GetAttemptCountAsync(Guid id) => Task.FromResult(Items.Count(x => x.OpenLoopId == id));
        public Task AddAsync(GuardianChaseAttempt attempt) { Items.Add(attempt); return Task.CompletedTask; }
        public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) => Task.FromResult(Items.Where(x => x.Status == status).ToList());
        public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid id) => Task.FromResult(Items.Where(x => x.OpenLoopId == id).ToList());
        public Task<bool> ApproveAsync(Guid id, DateTimeOffset at) => Task.FromResult(false);
        public Task RecordSendFailureAsync(Guid id, DateTimeOffset at, string error) => Task.CompletedTask;
        public Task<bool> MarkSentAsync(Guid id, DateTimeOffset sentAt, DateTimeOffset due) => Task.FromResult(false);
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent entry) { Items.Add(entry); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }
}
