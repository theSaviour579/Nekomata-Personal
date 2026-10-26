using Nekomata.Core.Guardian.Authority;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianActionAuthorityEngineTests
{
    [Fact]
    public void Safe_deterministic_reversible_action_can_auto_execute()
    {
        var decision=Engine().Evaluate(Request("AttributeMeetingEffort",100,["Confirmed Work Thread"]));

        Assert.Equal(GuardianActionAuthority.AutoExecute,decision.Authority);
        Assert.True(decision.CanExecute);
        Assert.NotEmpty(decision.UndoDescription);
    }

    [Fact]
    public void Preparing_a_message_does_not_grant_send_authority()
    {
        var prepared=Engine().Evaluate(Request("PrepareTeamsResponse",90,["Mature open loop"]));
        var send=Request("SendTeamsResponse",100,["User-visible draft"]);send.ChangesExternalSystem=true;send.AffectsAnotherPerson=true;
        var sending=Engine().Evaluate(send);

        Assert.Equal(GuardianActionAuthority.Prepare,prepared.Authority);
        Assert.Equal(GuardianActionAuthority.ApprovalRequired,sending.Authority);
        Assert.True(sending.RequiresApproval);
    }

    [Fact]
    public void Compliance_waiver_always_requires_approval()
    {
        var request=Request("WaiveComplianceRequirement",100,["Requirement owner identified"]);request.WaivesCompliance=true;
        Assert.Equal(GuardianActionAuthority.ApprovalRequired,Engine().Evaluate(request).Authority);
    }

    [Fact]
    public void Reassignment_always_requires_explicit_approval()
    {
        var request=Request("ReassignTaskOwner",100,["Skill and team match"]);request.ChangesExternalSystem=true;
        Assert.Equal(GuardianActionAuthority.ApprovalRequired,Engine().Evaluate(request).Authority);
    }

    [Fact]
    public void Project_state_change_remains_recommendation_only()
    {
        Assert.Equal(GuardianActionAuthority.Recommend,
            Engine().Evaluate(Request("ChangeProjectStatus",95,["External dependency evidenced"])).Authority);
    }

    [Fact]
    public void External_maintenance_requires_specific_pre_authorisation()
    {
        var request=Request("MaintainWorkThread",98,["Confirmed link evidence"]);
        request.ChangesExternalSystem=true;request.Impact=GuardianActionImpact.External;
        Assert.Equal(GuardianActionAuthority.ApprovalRequired,Engine().Evaluate(request).Authority);
        request.IsPreAuthorized=true;
        Assert.Equal(GuardianActionAuthority.AutoExecute,Engine().Evaluate(request).Authority);
    }

    [Fact]
    public void Sensitive_action_requires_approval_regardless_of_confidence()
    {
        var request=Request("UpdateSensitiveRecord",100,["Confirmed"]);request.Impact=GuardianActionImpact.Sensitive;
        Assert.Equal(GuardianActionAuthority.ApprovalRequired,Engine().Evaluate(request).Authority);
    }

    [Fact]
    public void Mutating_sql_is_prohibited_while_read_only_review_is_observed()
    {
        Assert.Equal(GuardianActionAuthority.Prohibited,Engine().Evaluate(Request("ExecuteSqlMutation",100,["Query"])).Authority);
        Assert.Equal(GuardianActionAuthority.Observe,Engine().Evaluate(Request("ObserveSqlValidation",100,["Explain plan"])).Authority);
    }

    [Fact]
    public void Missing_evidence_prevents_autonomous_execution()
    {
        var decision=Engine().Evaluate(Request("ResolveOpenLoop",100,[]));
        Assert.Equal(GuardianActionAuthority.Recommend,decision.Authority);
        Assert.False(decision.CanExecute);
    }

    [Fact]
    public async Task Recorded_decision_retains_authority_evidence_and_provenance()
    {
        var events=new FakeEvents();var source=Guid.NewGuid();var request=Request("SuppressDuplicate",95,["Matching source ID"]);
        request.SourceEventId=source;request.CorrelationId=Guid.NewGuid();

        await new GuardianActionAuthorityEngine(new WorkdayJournalService(events)).EvaluateAndRecordAsync(request);

        var entry=Assert.Single(events.Items);
        Assert.Equal("GuardianActionAuthorityEvaluated",entry.EventType);
        Assert.Equal(source,entry.ParentEventId);
        Assert.Equal("AutoExecute",entry.Outcome);
        Assert.Contains("Matching source ID",entry.Metadata);
    }

    private static GuardianActionAuthorityRequest Request(string type,int confidence,List<string> evidence)=>new()
    {
        ActionType=type,Title=type,Reason="Test policy evaluation",Confidence=confidence,IsDeterministic=true,
        Risk=GuardianActionRisk.Low,Reversibility=GuardianActionReversibility.Reversible,
        UndoDescription="Restore the previous state.",Evidence=evidence
    };

    private static GuardianActionAuthorityEngine Engine()=>new(new WorkdayJournalService(new FakeEvents()));

    private sealed class FakeEvents:IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[];
        public Task AddAsync(WorkdayEvent value){Items.Add(value);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(Items.Any(x=>x.EventType==type&&x.ExternalId==id));
    }
}
