using Nekomata.Core.Guardian.Learning;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class LearnedWorkflowEngineTests
{
    [Fact]
    public async Task Four_of_five_independent_data_threads_produces_inert_candidate_policy()
    {
        var learned=new FakeLearned(); var policies=new FakePolicies(); var events=new FakeEvents();
        var workspace=Workspace(5);
        for(var i=0;i<5;i++)
        {
            var thread=workspace.WorkThreads[i]; var project=workspace.Projects[i];
            events.Items.Add(Event(thread.Id,project.Id,"ObjectiveCompleted","Project complete"));
            if(i<4)
            {
                events.Items.Add(Event(thread.Id,project.Id,"TaskCompleted","Complete data validation"));
                events.Items.Add(Event(thread.Id,project.Id,"TaskCompleted","Validation repeated evidence"));
            }
        }
        var engine=new LearnedWorkflowEngine(learned,policies,events,new WorkdayJournalService(events));

        var patterns=await engine.ObserveAsync(workspace,DateTimeOffset.Parse("2026-08-20T10:00:00+01:00"));

        var pattern=Assert.Single(patterns,x=>x.TriggerType=="DataProject"&&x.ResultWorkType=="Validation");
        Assert.Equal(5,pattern.ComparableThreadCount);
        Assert.Equal(4,pattern.SupportThreadCount);
        Assert.Equal(1,pattern.ExceptionThreadCount);
        Assert.Equal(80,pattern.ConfidencePercent);
        Assert.Equal("Candidate",pattern.Lifecycle);
        var policy=Assert.Single(policies.Items);
        Assert.Equal("Candidate",policy.Lifecycle);
        Assert.Equal(5,policy.EvidenceCount);
    }

    [Fact]
    public async Task Candidate_becomes_active_only_after_explicit_promotion()
    {
        var learned=new FakeLearned(); var policies=new FakePolicies(); var events=new FakeEvents();
        var pattern=new LearnedWorkflowPattern { Id=Guid.NewGuid(),TriggerType="DataProject",ResultWorkType="Validation",Lifecycle="Candidate" };
        var policy=new GuardianPolicy { Id=Guid.NewGuid(),PolicyKey="learned:dataproject:validation",Lifecycle="Candidate",Name="Validation" };
        pattern.GuardianPolicyId=policy.Id; learned.Patterns.Add(pattern); policies.Items.Add(policy);
        var service=new LearnedWorkflowPromotionService(learned,policies,new WorkdayJournalService(events));

        Assert.True(await service.PromoteAsync(policy,"Reviewed and approved.",DateTimeOffset.UtcNow));

        Assert.Equal("Active",policy.Lifecycle);
        Assert.Equal("Promoted",pattern.Lifecycle);
        Assert.Single(learned.Decisions,x=>x.Decision=="Approved");
        Assert.Contains(events.Items,x=>x.EventType=="LearnedWorkflowRulePromoted"&&x.UserDecision=="Approved");
    }

    private static NekomataWorkspace Workspace(int count)
    {
        var workspace=new NekomataWorkspace();
        for(var i=1;i<=count;i++)
        {
            workspace.Projects.Add(new NekomataProject { Id=i,Name=$"Data Project {i}",Status="Completed" });
            workspace.WorkThreads.Add(new GuardianWorkThread { Id=Guid.NewGuid(),PrimaryProjectId=i,Title=$"Data Project {i}" });
        }
        return workspace;
    }
    private static WorkdayEvent Event(Guid thread,long project,string type,string title)=>new()
    { EventId=Guid.NewGuid(),CorrelationId=thread,ProjectId=project,EventType=type,Title=title,Narrative=title,OccurredAt=DateTimeOffset.UtcNow };

    private sealed class FakeLearned : ILearnedWorkflowRepository
    {
        public List<WorkflowObservation> Observations{get;}=[]; public List<LearnedWorkflowPattern> Patterns{get;}=[]; public List<PolicyPromotionDecision> Decisions{get;}=[];
        public Task UpsertObservationAsync(WorkflowObservation value){var old=Observations.FirstOrDefault(x=>x.WorkThreadId==value.WorkThreadId&&x.TriggerType==value.TriggerType&&x.ResultWorkType==value.ResultWorkType);if(old is not null)Observations.Remove(old);Observations.Add(value);return Task.CompletedTask;}
        public Task<List<WorkflowObservation>> GetObservationsAsync()=>Task.FromResult(Observations.ToList());
        public Task<LearnedWorkflowPattern> UpsertPatternAsync(LearnedWorkflowPattern value){var old=Patterns.FirstOrDefault(x=>x.TriggerType==value.TriggerType&&x.ResultWorkType==value.ResultWorkType);if(old is not null){value.Id=old.Id;Patterns.Remove(old);}Patterns.Add(value);return Task.FromResult(value);}
        public Task AddPromotionDecisionAsync(PolicyPromotionDecision value){Decisions.Add(value);return Task.CompletedTask;}
        public Task<LearnedWorkflowPattern?> FindByPolicyIdAsync(Guid id)=>Task.FromResult(Patterns.FirstOrDefault(x=>x.GuardianPolicyId==id));
        public Task MarkPromotedAsync(Guid id,Guid policyId){var item=Patterns.Single(x=>x.Id==id);item.Lifecycle="Promoted";item.GuardianPolicyId=policyId;return Task.CompletedTask;}
    }
    private sealed class FakePolicies : IGuardianPolicyRepository
    {
        public List<GuardianPolicy> Items{get;}=[];
        public Task<List<GuardianPolicy>> GetAllAsync()=>Task.FromResult(Items.ToList());
        public Task<GuardianPolicy> UpsertEvidenceAsync(GuardianPolicy value){var old=Items.FirstOrDefault(x=>x.PolicyKey==value.PolicyKey);if(old is not null){value.Id=old.Id;Items.Remove(old);}Items.Add(value);return Task.FromResult(value);}
        public Task SetLifecycleAsync(Guid id,string lifecycle){Items.Single(x=>x.Id==id).Lifecycle=lifecycle;return Task.CompletedTask;}
        public Task AddApplicationsAsync(IEnumerable<Guid> ids,long? task,long? project,bool counterfactual)=>Task.CompletedTask;
        public Task<bool> HasApplicationAsync(Guid policyId,long? taskId)=>Task.FromResult(false);
        public Task CompleteApplicationsAsync(IEnumerable<Guid> ids,long? task,long? project,string outcome,int minutes)=>Task.CompletedTask;
    }
    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[];
        public Task AddAsync(WorkdayEvent value){Items.Add(value);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(Items.Any(x=>x.EventType==type&&x.ExternalId==id));
    }
}
