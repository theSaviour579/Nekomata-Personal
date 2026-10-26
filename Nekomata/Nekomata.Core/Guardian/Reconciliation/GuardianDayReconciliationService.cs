using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Reconciliation;

public sealed class GuardianDayReconciliationService(WorkdayJournalService journal)
{
    public async Task<GuardianDayReconciliationResult> ReconcileAsync(
        NekomataWorkspace previous,NekomataWorkspace current,DateTimeOffset now)
    {
        var result=new GuardianDayReconciliationResult { ReconciledAt=now };
        var previousTasks=previous.Tasks.ToDictionary(x=>x.Id);
        var currentTasks=current.Tasks.ToDictionary(x=>x.Id);
        foreach(var task in currentTasks.Values.Where(x=>!previousTasks.ContainsKey(x.Id)))
            result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Routine,ChangeType="TaskAppeared",
                Summary=$"New active task: {task.Title}",TaskId=task.Id});
        foreach(var task in previousTasks.Values.Where(x=>!currentTasks.ContainsKey(x.Id)))
            result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Routine,ChangeType="TaskNoLongerOpen",
                Summary=$"Task is no longer active: {task.Title}",TaskId=task.Id});

        var previousCommitments=previous.NewCommitments.Select(x=>x.SignalId).ToHashSet();
        foreach(var commitment in current.NewCommitments.Where(x=>!previousCommitments.Contains(x.SignalId)))
            result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Exception,ChangeType="NewCommitment",
                Summary=$"New {commitment.Source} commitment needs review: {commitment.Title}",WorkThreadId=commitment.WorkThreadId});

        foreach(var thread in current.WorkThreadSummaries)
        {
            var before=previous.WorkThreadSummaries.FirstOrDefault(x=>x.ThreadId==thread.ThreadId);
            if(before is null) continue;
            if(before.BlockingDependencies==0&&thread.BlockingDependencies>0)
                result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Material,ChangeType="ThreadBlocked",
                    Summary=$"{thread.Title} became blocked.",WorkThreadId=thread.ThreadId});
            if(before.BlockingDependencies>0&&thread.BlockingDependencies==0)
                result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Routine,ChangeType="ThreadUnblocked",
                    Summary=$"{thread.Title} is no longer blocked.",WorkThreadId=thread.ThreadId});
            if(!before.Impact.HasMatureOpenLoop&&thread.Impact.HasMatureOpenLoop)
                result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Exception,ChangeType="DependencyMatured",
                    Summary=$"A dependency on {thread.Title} now needs intervention.",WorkThreadId=thread.ThreadId});
        }

        var previousMissing=previous.MissingWorkCandidates.Select(x=>$"{x.SourceType}:{x.SourceKey}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var missing in current.MissingWorkCandidates.Where(x=>!previousMissing.Contains($"{x.SourceType}:{x.SourceKey}")))
            result.Changes.Add(new(){Kind=GuardianReconciliationChangeKind.Exception,ChangeType="MissingWorkDetected",
                Summary=$"Required work is unbound: {missing.Title}",WorkThreadId=missing.ThreadId});

        if(result.Changes.Count>0)
        {
            var bucket=new DateTimeOffset(now.Year,now.Month,now.Day,now.Hour,now.Minute/5*5,0,now.Offset);
            var eventId=StableId("day-reconciliation",bucket.ToUniversalTime().ToString("O"));
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId=eventId,CorrelationId=eventId,OccurredAt=now,EventType="GuardianDayReconciled",Category="Reconciliation",
                Title=result.RequiresAttention?"Workspace reconciled — attention required":"Workspace reconciled — no action required",
                Narrative=result.Summary,ExternalId=eventId.ToString("N"),Outcome=result.RequiresAttention?"ExceptionsDetected":"RoutineOnly",
                Significance=result.MaterialCount>0?80:result.ExceptionCount>0?65:35,Metadata=JsonSerializer.Serialize(result.Changes)
            });
        }
        return result;
    }

    private static Guid StableId(string type,string key)=>new(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{key}"))[..16]);
}
