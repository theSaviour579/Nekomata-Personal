using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Authority;

public sealed class GuardianActionAuthorityEngine(WorkdayJournalService journal,
    IGuardianDecisionOutcomeRepository? outcomes = null,
    IGuardianAutoExecutionNotifier? autoExecutionNotifier = null)
{
    private static readonly HashSet<string> SafeDeterministicActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "LinkEvidence", "AttributeMeetingEffort", "CreateComplianceTask", "ResolveOpenLoop",
        "ReplanInternalWork", "UpdateDerivedEstimate", "SuppressDuplicate", "MaintainWorkThread"
    };

    public GuardianActionAuthorityDecision Evaluate(GuardianActionAuthorityRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionType);
        var (authority, explanation) = Classify(request);
        var decision = new GuardianActionAuthorityDecision
        {
            ActionType=request.ActionType,Authority=authority,Risk=request.Risk,Reversibility=request.Reversibility,
            Confidence=Math.Clamp(request.Confidence,0,100),Reason=request.Reason,PolicyExplanation=explanation,
            UndoDescription=request.UndoDescription,Evidence=[..request.Evidence],SourceEventId=request.SourceEventId
        };
        decision.Id = StableId("authority-decision", $"{request.SourceEventId}:{request.ActionType}:{decision.Authority}");
        return decision;
    }

    public async Task<GuardianActionAuthorityDecision> EvaluateAndRecordAsync(GuardianActionAuthorityRequest request)
    {
        var decision=Evaluate(request);
        var eventId=StableId("action-authority",$"{request.SourceEventId}:{request.ActionType}:{decision.Authority}");
        var recordedNewly = await journal.RecordIfAbsentAsync(new WorkdayEvent
        {
            EventId=eventId,CorrelationId=request.CorrelationId==Guid.Empty?eventId:request.CorrelationId,
            ParentEventId=request.SourceEventId==Guid.Empty?null:request.SourceEventId,OccurredAt=DateTimeOffset.UtcNow,
            EventType="GuardianActionAuthorityEvaluated",Category="Authority",Title=request.Title,
            Narrative=$"{decision.Authority}: {decision.PolicyExplanation} Reason: {request.Reason}",
            ProjectId=request.ProjectId,TaskId=request.TaskId,ExternalId=eventId.ToString("N"),Outcome=decision.Authority.ToString(),
            Significance=decision.Risk>=GuardianActionRisk.High?75:45,
            Metadata=JsonSerializer.Serialize(new {decision.ActionType,decision.Authority,decision.Risk,decision.Reversibility,
                decision.Confidence,decision.Evidence,decision.UndoDescription})
        });
        if (outcomes is not null)
            await outcomes.UpsertAsync(new GuardianDecisionOutcome
            {
                Id=decision.Id,ActionType=decision.ActionType,Recommendation=request.Title,Authority=decision.Authority,
                Confidence=decision.Confidence,Decision=decision.Authority==GuardianActionAuthority.AutoExecute
                    ? GuardianDecisionDisposition.AutoExecuted : GuardianDecisionDisposition.Proposed,
                DecisionReason=decision.PolicyExplanation,SourceEventId=request.SourceEventId,
                ProjectId=request.ProjectId,TaskId=request.TaskId,RecommendedAt=DateTimeOffset.UtcNow
            });
        if (recordedNewly && decision.Authority == GuardianActionAuthority.AutoExecute && autoExecutionNotifier is not null)
        {
            try { await autoExecutionNotifier.NotifyAutoExecutedAsync(decision, request); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Auto-execution acknowledgement failed safely: {ex}"); }
        }
        return decision;
    }

    private static (GuardianActionAuthority Authority,string Explanation) Classify(GuardianActionAuthorityRequest request)
    {
        if(request.IsProhibited) return (GuardianActionAuthority.Prohibited,"An explicit safety policy prohibits this action.");
        if(request.Impact==GuardianActionImpact.Sensitive) return (GuardianActionAuthority.ApprovalRequired,
            "Sensitive or high-impact actions always require explicit human approval.");
        if(IsSqlMutation(request.ActionType)) return (GuardianActionAuthority.Prohibited,
            "Guardian may validate report SQL read-only, but may not execute a mutating query.");
        if(request.WaivesCompliance) return (GuardianActionAuthority.ApprovalRequired,"Compliance requirements may only be waived by an authorised person.");
        if(IsExternalCommunication(request.ActionType)) return (GuardianActionAuthority.ApprovalRequired,
            "External communications remain draft-only until a person explicitly approves sending.");
        if(IsReassignment(request.ActionType)) return (GuardianActionAuthority.ApprovalRequired,
            "Changing who owns a piece of work affects another person and always requires explicit approval.");
        if(IsProjectStateChange(request.ActionType)) return request.Confidence>=60
            ? (GuardianActionAuthority.Recommend,"Project state changes are advisory; Guardian may explain but not prepare or apply them autonomously.")
            : (GuardianActionAuthority.Observe,"Project-state evidence is too weak to recommend a change.");
        if(request.IsPreAuthorized&&SafeDeterministicActions.Contains(request.ActionType)&&request.IsDeterministic&&request.Confidence>=90&&
           request.Risk<=GuardianActionRisk.Medium&&request.Reversibility!=GuardianActionReversibility.Irreversible&&request.Evidence.Count>0)
            return (GuardianActionAuthority.AutoExecute,"A specific pre-authorisation permits this deterministic, bounded and reversible action.");
        if(request.ChangesExternalSystem||request.AffectsAnotherPerson)
            return (GuardianActionAuthority.ApprovalRequired,"The action affects another person or materially changes an external system.");
        if(request.ActionType.Contains("Prepare",StringComparison.OrdinalIgnoreCase)||request.ActionType.Contains("Draft",StringComparison.OrdinalIgnoreCase))
            return (GuardianActionAuthority.Prepare,"Guardian may prepare this reversible artefact but may not send or apply it.");
        if(SafeDeterministicActions.Contains(request.ActionType)&&request.IsDeterministic&&request.Confidence>=90&&
           request.Risk<=GuardianActionRisk.Medium&&request.Reversibility!=GuardianActionReversibility.Irreversible&&request.Evidence.Count>0)
            return request.Impact<=GuardianActionImpact.Internal
                ? (GuardianActionAuthority.AutoExecute,"An established internal policy permits this deterministic, bounded and reversible action.")
                : (GuardianActionAuthority.Prepare,"External impact requires explicit pre-authorisation even though the action is high-confidence and reversible.");
        if(request.ActionType.StartsWith("Observe",StringComparison.OrdinalIgnoreCase))
            return (GuardianActionAuthority.Observe,"The action only records an observation and changes no operational state.");
        if(request.Confidence>=85&&request.Risk<=GuardianActionRisk.Medium&&request.Evidence.Count>0&&
           request.Reversibility!=GuardianActionReversibility.Irreversible)
            return (GuardianActionAuthority.Prepare,"High-confidence, reversible evidence permits Guardian to prepare the action for one-click approval.");
        return request.Confidence>=60
            ? (GuardianActionAuthority.Recommend,"Evidence supports a recommendation, but autonomous authority is insufficient.")
            : (GuardianActionAuthority.Observe,"Confidence is insufficient to recommend or act.");
    }

    private static Guid StableId(string type,string key)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{key}"))[..16]);
    private static bool IsExternalCommunication(string value)=>value.Contains("SendEmail",StringComparison.OrdinalIgnoreCase)||
        value.Contains("SendMessage",StringComparison.OrdinalIgnoreCase)||value.Contains("ExternalCommunication",StringComparison.OrdinalIgnoreCase);
    private static bool IsReassignment(string value)=>value.Contains("Reassign",StringComparison.OrdinalIgnoreCase)||
        value.Contains("ChangeOwner",StringComparison.OrdinalIgnoreCase);
    private static bool IsProjectStateChange(string value)=>value.Contains("Project",StringComparison.OrdinalIgnoreCase)&&
        (value.Contains("State",StringComparison.OrdinalIgnoreCase)||value.Contains("Status",StringComparison.OrdinalIgnoreCase));
    private static bool IsSqlMutation(string value)=>value.Contains("Sql",StringComparison.OrdinalIgnoreCase)&&
        (value.Contains("Execute",StringComparison.OrdinalIgnoreCase)||value.Contains("Mutate",StringComparison.OrdinalIgnoreCase));
}
