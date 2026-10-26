using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Exceptions;

public sealed class GuardianExceptionManagementService(IWorkdayEventRepository journal)
{
    public async Task<GuardianExceptionManagementResult> EvaluateAsync(
        GuardianDayReconciliationResult reconciliation,NekomataWorkspace workspace,DateTimeOffset now)
    {
        var result=new GuardianExceptionManagementResult { RoutineActionsHandled=reconciliation.RoutineCount };
        foreach(var change in reconciliation.Changes.Where(x=>x.Kind!=GuardianReconciliationChangeKind.Routine))
            Add(result,new GuardianOperationalException
            {
                Key=$"reconcile:{change.ChangeType}:{change.WorkThreadId}:{change.TaskId}",
                Category=change.Kind==GuardianReconciliationChangeKind.Material
                    ?GuardianExceptionCategory.MaterialPlanningChange:Category(change.ChangeType),
                Severity=change.Kind==GuardianReconciliationChangeKind.Material?"High":"Warning",
                Title=change.Kind==GuardianReconciliationChangeKind.Material?"Material workspace change":"Guardian needs review",
                Detail=change.Summary,ActionKind=change.ChangeType=="MissingWorkDetected"?"open_calendar":"open_dashboard",
                ContextId=change.WorkThreadId?.ToString("N")??"",WorkThreadId=change.WorkThreadId
            });

        foreach(var requirement in workspace.ProcessRequirements.Where(x=>x.Gate!=ReadinessGate.None&&
                    x.State is RequirementState.Blocked or RequirementState.EvidenceRequired))
            Add(result,new GuardianOperationalException
            {
                Key=$"compliance:{requirement.Id:N}",Category=GuardianExceptionCategory.ComplianceRisk,Severity="High",
                Title=$"{requirement.Gate} readiness at risk",Detail=$"{requirement.Name}: {requirement.Reason}",
                ContextId=requirement.WorkThreadId.ToString("N"),WorkThreadId=requirement.WorkThreadId
            });

        foreach(var thread in workspace.WorkThreadSummaries.Where(x=>x.Impact.HasMatureOpenLoop&&
                    (x.Impact.IsBlockingCurrentObjective||x.Impact.IsBlockingActiveTask)))
            Add(result,new GuardianOperationalException
            {
                Key=$"dependency:{thread.ThreadId:N}",Category=GuardianExceptionCategory.HighImpactDependency,Severity="High",
                Title=$"High-impact dependency: {thread.Title}",Detail=thread.HighestImpactDependency,
                ContextId=thread.ThreadId.ToString("N"),WorkThreadId=thread.ThreadId
            });

        foreach(var item in workspace.UnboundContext
                    .Where(x=>x.Reason.Contains("requires confirmation",StringComparison.OrdinalIgnoreCase))
                    .GroupBy(x=>$"{x.EntityType}:{x.EntityKey}",StringComparer.OrdinalIgnoreCase)
                    .Select(x=>x.First()).Take(5))
            Add(result,new GuardianOperationalException
            {
                Key=$"context:{item.EntityType}:{item.EntityKey}",Category=GuardianExceptionCategory.AmbiguousContext,
                Title="Ambiguous work context",Detail=$"{item.Title} could not be safely assigned to a Work Thread.",
                ActionKind=item.EntityType=="CalendarEvent"?"open_calendar":"open_dashboard",ContextId=item.EntityKey
            });

        var recent=await journal.GetBetweenAsync(now.AddMinutes(-15),now.AddMinutes(1),500);
        foreach(var entry in recent.Where(x=>x.EventType=="GuardianActionAuthorityEvaluated"&&x.Outcome=="ApprovalRequired"))
            Add(result,new GuardianOperationalException
            {
                Key=$"approval:{entry.EventId:N}",Category=GuardianExceptionCategory.ApprovalRequired,Severity="Warning",
                Title="Approval required",Detail=entry.Narrative,ContextId=entry.CorrelationId.ToString("N")
            });
        foreach(var entry in recent.Where(x=>x.Outcome.Contains("Fail",StringComparison.OrdinalIgnoreCase)||
                    x.EventType.Contains("Failed",StringComparison.OrdinalIgnoreCase)))
            Add(result,new GuardianOperationalException
            {
                Key=$"failure:{entry.EventId:N}",Category=GuardianExceptionCategory.ActionFailed,Severity="High",
                Title="Guardian could not complete an action",Detail=entry.Narrative,ContextId=entry.CorrelationId.ToString("N")
            });
        return result;
    }

    private static GuardianExceptionCategory Category(string type)=>type switch
    {
        "NewCommitment"=>GuardianExceptionCategory.DecisionRequired,
        "MissingWorkDetected"=>GuardianExceptionCategory.AmbiguousContext,
        "DependencyMatured"=>GuardianExceptionCategory.HighImpactDependency,
        _=>GuardianExceptionCategory.DecisionRequired
    };

    private static void Add(GuardianExceptionManagementResult result,GuardianOperationalException item)
    {
        if(!result.Exceptions.Any(x=>x.Key.Equals(item.Key,StringComparison.OrdinalIgnoreCase)))result.Exceptions.Add(item);
    }
}
