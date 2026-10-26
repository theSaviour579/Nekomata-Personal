using Nekomata.Core.Guardian.Context;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class ContextSignalResolutionServiceTests
{
    [Fact]
    public async Task Existing_local_task_suppresses_external_duplicate()
    {
        var signal=new ContextSignal { Source="Microsoft To Do",Kind=ContextSignalKind.AssignedWork,ExternalId="1",Title="Complete purchasing review",SourceEventId=Guid.NewGuid() };
        var signals=new FakeSignals(signal);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace { Tasks=[new NekomataTask { Id=12,Title="Complete Purchasing Review" }],WorkThreads=[threads.Thread] };

        await new ContextSignalResolutionService(signals,threads,new WorkdayJournalService(events)).ResolveAsync(workspace,DateTimeOffset.UtcNow);

        Assert.Empty(workspace.NewCommitments);
        Assert.Equal(ContextSignalDisposition.Duplicate,signal.Disposition);
        Assert.Equal(12,signal.ExistingTaskId);
    }

    [Fact]
    public async Task New_planner_work_is_correlated_but_not_silently_imported()
    {
        var signal=new ContextSignal { Source="Microsoft Planner",Kind=ContextSignalKind.AssignedWork,ExternalId="p1",
            Title="Purchasing Review supplier validation",Owner="Shared assignment",SourceEventId=Guid.NewGuid(),ObservedAt=DateTimeOffset.UtcNow };
        var signals=new FakeSignals(signal);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace { Projects=[new NekomataProject { Id=7,Name="Purchasing Review" }],WorkThreads=[threads.Thread] };

        await new ContextSignalResolutionService(signals,threads,new WorkdayJournalService(events)).ResolveAsync(workspace,DateTimeOffset.UtcNow);

        var commitment=Assert.Single(workspace.NewCommitments);
        Assert.Equal(threads.Thread.Id,commitment.WorkThreadId);
        Assert.Equal(ContextSignalDisposition.Correlated,signal.Disposition);
        Assert.Null(signal.ExistingTaskId);
        Assert.Contains(threads.Thread.Links,x=>x.EntityType=="Planner"&&x.Status==WorkThreadLinkStatus.Candidate);
        Assert.Contains(events.Items,x=>x.EventType=="ExternalCommitmentDetected");
    }

    private sealed class FakeSignals(params ContextSignal[] values):IContextSignalRepository
    {
        private readonly List<ContextSignal> _items=[..values];
        public Task<ContextSignal> UpsertAsync(ContextSignal value){_items.Add(value);return Task.FromResult(value);}
        public Task<List<ContextSignal>> GetCurrentAsync(int count=1000)=>Task.FromResult(_items.Take(count).ToList());
        public Task ResolveAsync(Guid id,ContextSignalDisposition disposition,Guid? thread,long? task,string resolution){var value=_items.Single(x=>x.Id==id);value.Disposition=disposition;value.WorkThreadId=thread;value.ExistingTaskId=task;value.Resolution=resolution;return Task.CompletedTask;}
    }
    private sealed class FakeThreads:IGuardianWorkThreadRepository
    {
        public GuardianWorkThread Thread{get;}=new(){Id=Guid.NewGuid(),Title="Purchasing Review",PrimaryProjectId=7};
        public Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink link){Thread.Links.Add(link);return Task.FromResult(link);}
        public Task<List<GuardianWorkThread>> GetActiveAsync()=>Task.FromResult(new List<GuardianWorkThread>{Thread});
        public Task<GuardianWorkThread> GetOrCreateForProjectAsync(long id,string title,Guid source)=>Task.FromResult(Thread);
        public Task AddEvidenceAsync(GuardianCorrelationEvidence value)=>Task.CompletedTask;
        public Task<GuardianWorkThreadLink?> FindLinkAsync(string type,string key,WorkThreadLinkStatus? status=null)=>Task.FromResult(Thread.Links.FirstOrDefault());
        public Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid id)=>Task.FromResult(new List<GuardianCorrelationEvidence>());
        public Task UpdateEvaluationAsync(Guid id,int confidence,WorkThreadLinkStatus status,string method)=>Task.CompletedTask;
    }
    private sealed class FakeEvents:IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[];public Task AddAsync(WorkdayEvent value){Items.Add(value);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(Items.Any(x=>x.EventType==type&&x.ExternalId==id));
    }
}
