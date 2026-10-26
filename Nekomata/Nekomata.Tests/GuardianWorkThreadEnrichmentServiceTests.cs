using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianWorkThreadEnrichmentServiceTests
{
    [Fact]
    public async Task Summary_uses_confirmed_links_for_effort_dependencies_and_impact()
    {
        var loop = new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), ProjectId = 7, TaskId = 12, Type = OpenLoopType.AwaitingThem,
            WaitingOn = "Phil", CreatedAt = DateTimeOffset.Parse("2026-08-17T09:00:00+01:00"),
            SourceEventId = Guid.NewGuid()
        };
        var thread = new GuardianWorkThread
        {
            Id = Guid.NewGuid(), Title = "Purchasing Review", PrimaryProjectId = 7,
            Links =
            [
                Link("Task", "12", WorkThreadLinkStatus.Confirmed),
                Link("OpenLoop", loop.Id.ToString(), WorkThreadLinkStatus.Confirmed, "Blocks"),
                Link("Email", "msg-5721", WorkThreadLinkStatus.Confirmed),
                Link("CalendarEvent", "meeting-1", WorkThreadLinkStatus.Confirmed),
                Link("TeamsMessage", "message-1", WorkThreadLinkStatus.Candidate)
            ]
        };
        var workspace = new NekomataWorkspace
        {
            Projects = [new NekomataProject { Id = 7, Name = "Purchasing Review Program", Priority = "High", EstimatedBusinessValue = 50000 }],
            Tasks = [new NekomataTask { Id = 12, ProjectId = 7, Title = "Database handover", EstimatedMinutes = 240, ActualMinutes = 75 }],
            CurrentMission = new Mission { ProjectId = 7 }, WorkThreads = [thread],
            UnboundContext = [new GuardianUnboundContext { EntityType = "TeamsMessage", EntityKey = "message-1" }]
        };
        var loops = new FakeLoops(loop);
        var events = new FakeEvents();
        var journal = new WorkdayJournalService(events);
        var service = new GuardianWorkThreadEnrichmentService(loops,
            new GuardianOpenLoopAgeService(loops, journal), events);

        var summary = Assert.Single(await service.BuildAsync(workspace, DateTimeOffset.Parse("2026-08-20T10:00:00+01:00")));

        Assert.Equal(1, summary.ActiveTasks);
        Assert.Equal(1, summary.Meetings);
        Assert.Equal(1, summary.OpenLoops);
        Assert.Equal(240, summary.PlannedEffortMinutes);
        Assert.Equal(75, summary.RecordedEffortMinutes);
        Assert.Equal(165, summary.RemainingEffortMinutes);
        Assert.Equal("Waiting on Phil", summary.HighestImpactDependency);
        Assert.True(summary.Impact.IsBlockingCurrentObjective);
        Assert.True(summary.Impact.IsBlockingActiveTask);
        Assert.True(summary.Impact.HasMatureOpenLoop);
        Assert.True(summary.Impact.HasUnboundRequiredWork);
        Assert.Equal(50000, summary.Impact.BusinessValue);
    }

    private static GuardianWorkThreadLink Link(string type, string key, WorkThreadLinkStatus status, string relationship = "BelongsTo") => new()
    { Id = Guid.NewGuid(), EntityType = type, EntityKey = key, Status = status, RelationshipType = relationship, CreatedAt = DateTimeOffset.UtcNow };

    private sealed class FakeLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop loop) => Task.FromResult(loop);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent entry) { Items.Add(entry); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.Where(x => x.OccurredAt >= from && x.OccurredAt < to).ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }
}
