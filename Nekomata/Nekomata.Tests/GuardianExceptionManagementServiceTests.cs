using Nekomata.Core.Guardian.Exceptions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianExceptionManagementServiceTests
{
    [Fact]
    public async Task Routine_changes_are_summarised_without_creating_exceptions()
    {
        var reconciliation=new GuardianDayReconciliationResult
        {Changes=[new(){Kind=GuardianReconciliationChangeKind.Routine,ChangeType="TaskAppeared",Summary="Routine"}]};
        var result=await new GuardianExceptionManagementService(new FakeEvents()).EvaluateAsync(
            reconciliation,new NekomataWorkspace(),DateTimeOffset.UtcNow);

        Assert.Equal(1,result.RoutineActionsHandled);
        Assert.Empty(result.Exceptions);
        Assert.Contains("1 routine change",result.ActivitySummary);
    }

    [Fact]
    public async Task Compliance_gate_and_material_change_become_deduplicated_exceptions()
    {
        var threadId=Guid.NewGuid();var requirementId=Guid.NewGuid();
        var reconciliation=new GuardianDayReconciliationResult
        {Changes=[new(){Kind=GuardianReconciliationChangeKind.Material,ChangeType="ThreadBlocked",Summary="Purchasing became blocked",WorkThreadId=threadId}]};
        var workspace=new NekomataWorkspace { ProcessRequirements=[new ProcessRequirement
        {Id=requirementId,WorkThreadId=threadId,Name="ECSV",Reason="Evidence absent",Gate=ReadinessGate.Closure,State=RequirementState.EvidenceRequired}]};

        var result=await new GuardianExceptionManagementService(new FakeEvents()).EvaluateAsync(reconciliation,workspace,DateTimeOffset.UtcNow);

        Assert.Equal(2,result.Exceptions.Count);
        Assert.Contains(result.Exceptions,x=>x.Category==GuardianExceptionCategory.MaterialPlanningChange);
        Assert.Contains(result.Exceptions,x=>x.Category==GuardianExceptionCategory.ComplianceRisk&&x.Severity=="High");
    }

    [Fact]
    public async Task Approval_and_failure_authority_events_are_promoted()
    {
        var now=DateTimeOffset.UtcNow;var events=new FakeEvents(
            new WorkdayEvent{EventType="GuardianActionAuthorityEvaluated",Outcome="ApprovalRequired",OccurredAt=now,EventId=Guid.NewGuid(),Narrative="Sending affects another person"},
            new WorkdayEvent{EventType="GuardianActionFailed",Outcome="Failed",OccurredAt=now,EventId=Guid.NewGuid(),Narrative="Graph send failed"});

        var result=await new GuardianExceptionManagementService(events).EvaluateAsync(
            new GuardianDayReconciliationResult(),new NekomataWorkspace(),now);

        Assert.Contains(result.Exceptions,x=>x.Category==GuardianExceptionCategory.ApprovalRequired);
        Assert.Contains(result.Exceptions,x=>x.Category==GuardianExceptionCategory.ActionFailed);
    }

    private sealed class FakeEvents(params WorkdayEvent[] entries):IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[..entries];
        public Task AddAsync(WorkdayEvent value){Items.Add(value);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>
            Task.FromResult(Items.Where(x=>x.OccurredAt>=from&&x.OccurredAt<to).ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(false);
    }
}
