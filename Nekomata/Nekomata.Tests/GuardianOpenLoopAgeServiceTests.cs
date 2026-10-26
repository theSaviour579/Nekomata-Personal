using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianOpenLoopAgeServiceTests
{
    [Theory]
    [InlineData("2026-08-17T09:00:00+01:00", "2026-08-19T09:00:00+01:00", 2)]
    [InlineData("2026-08-14T09:00:00+01:00", "2026-08-18T09:00:00+01:00", 2)]
    [InlineData("2026-08-15T09:00:00+01:00", "2026-08-17T09:00:00+01:00", 1)]
    public void Counts_working_days_not_elapsed_days(string created, string now, int expected) =>
        Assert.Equal(expected, GuardianOpenLoopAgeService.WorkingDaysBetween(DateTimeOffset.Parse(created), DateTimeOffset.Parse(now)));

    [Fact]
    public async Task Mature_loop_generates_one_idempotent_chronicle_event()
    {
        var loop = new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Type = OpenLoopType.AwaitingThem, Status = OpenLoopStatus.Open,
            CreatedAt = DateTimeOffset.Parse("2026-08-17T09:00:00+01:00"),
            WaitingOn = "Phil", Title = "Purchasing Review", SourceEventId = Guid.NewGuid()
        };
        var loops = new FakeOpenLoops(loop);
        var events = new FakeEvents();
        var service = new GuardianOpenLoopAgeService(loops, new WorkdayJournalService(events));
        var now = DateTimeOffset.Parse("2026-08-19T10:00:00+01:00");

        var first = await service.EvaluateAsync(now);
        await service.EvaluateAsync(now.AddHours(1));

        var chase = Assert.Single(first);
        Assert.True(chase.ChaseMature);
        Assert.Equal("You've been waiting on Phil for 2 working days.", chase.Message);
        Assert.Single(events.Items);
        Assert.Equal("OpenLoopChaseMatured", events.Items[0].EventType);
    }

    private sealed class FakeOpenLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop openLoop) => Task.FromResult(openLoop);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(loops.Where(x => x.Type == OpenLoopType.AwaitingThem).ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent entry) { Items.Add(entry); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string eventType, string externalId) => Task.FromResult(Items.Any(x => x.EventType == eventType && x.ExternalId == externalId));
    }
}
