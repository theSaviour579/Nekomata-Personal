using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.Reconciliation;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianDayReconciliationServiceTests
{
    [Fact]
    public async Task Routine_task_changes_are_consolidated_without_attention()
    {
        var events=new FakeEvents();var previous=WorkspaceWithTask(1,"Old task");var current=WorkspaceWithTask(2,"New task");

        var result=await new GuardianDayReconciliationService(new WorkdayJournalService(events))
            .ReconcileAsync(previous,current,DateTimeOffset.Parse("2026-08-20T10:01:00+01:00"));

        Assert.Equal(2,result.RoutineCount);
        Assert.False(result.RequiresAttention);
        var entry=Assert.Single(events.Items);
        Assert.Equal("RoutineOnly",entry.Outcome);
        Assert.Contains("2 routine",entry.Narrative);
    }

    [Fact]
    public async Task Newly_blocked_thread_is_a_material_exception()
    {
        var id=Guid.NewGuid();var previous=new NekomataWorkspace
        {WorkThreadSummaries=[new GuardianWorkThreadSummary{ThreadId=id,Title="Purchasing",BlockingDependencies=0}]};
        var current=new NekomataWorkspace
        {WorkThreadSummaries=[new GuardianWorkThreadSummary{ThreadId=id,Title="Purchasing",BlockingDependencies=1}]};
        var events=new FakeEvents();

        var result=await new GuardianDayReconciliationService(new WorkdayJournalService(events))
            .ReconcileAsync(previous,current,DateTimeOffset.Parse("2026-08-20T10:06:00+01:00"));

        Assert.Equal(1,result.MaterialCount);
        Assert.True(result.RequiresAttention);
        Assert.Equal("ExceptionsDetected",Assert.Single(events.Items).Outcome);
    }

    [Fact]
    public async Task Unchanged_workspace_creates_no_activity_noise()
    {
        var workspace=WorkspaceWithTask(1,"Stable");var events=new FakeEvents();
        var result=await new GuardianDayReconciliationService(new WorkdayJournalService(events))
            .ReconcileAsync(workspace,workspace,DateTimeOffset.UtcNow);
        Assert.Empty(result.Changes);
        Assert.Empty(events.Items);
    }

    private static NekomataWorkspace WorkspaceWithTask(long id,string title)=>new()
    {Tasks=[new NekomataTask{Id=id,Title=title}]};

    private sealed class FakeEvents:IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[];
        public Task AddAsync(WorkdayEvent value){Items.Add(value);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(Items.Any(x=>x.EventType==type&&x.ExternalId==id));
    }
}
