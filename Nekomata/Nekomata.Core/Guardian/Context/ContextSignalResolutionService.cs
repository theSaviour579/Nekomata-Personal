using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Context;

public sealed class ContextSignalResolutionService(
    IContextSignalRepository signals,
    IGuardianWorkThreadRepository threads,
    WorkdayJournalService journal)
{
    public async Task<IReadOnlyList<GuardianNewCommitment>> ResolveAsync(NekomataWorkspace workspace,DateTimeOffset now)
    {
        var current=await signals.GetCurrentAsync();
        var commitments=new List<GuardianNewCommitment>();
        foreach(var signal in current)
        {
            if(signal.Kind!=ContextSignalKind.AssignedWork) continue;
            var duplicate=workspace.Tasks.FirstOrDefault(task=>Equivalent(task.Title,signal.Title));
            if(duplicate is not null)
            {
                var thread=workspace.WorkThreads.FirstOrDefault(x=>x.Links.Any(link=>link.Status==WorkThreadLinkStatus.Confirmed&&
                    link.EntityType=="Task"&&link.EntityKey==duplicate.Id.ToString()));
                await signals.ResolveAsync(signal.Id,ContextSignalDisposition.Duplicate,thread?.Id,duplicate.Id,
                    $"Suppressed because local task {duplicate.Id} has the same normalized title.");
                continue;
            }

            var candidates=workspace.WorkThreads.Select(thread=>(Thread:thread,Project:thread.PrimaryProjectId is long id?workspace.Projects.FirstOrDefault(x=>x.Id==id):null))
                .Where(x=>x.Project is not null&&($"{signal.Title} {signal.Body}").Contains(x.Project!.Name,StringComparison.OrdinalIgnoreCase)).ToList();
            GuardianWorkThread? matched=candidates.Count==1?candidates[0].Thread:null;
            if(matched is not null)
            {
                await threads.UpsertLinkAsync(new GuardianWorkThreadLink
                {
                    ThreadId=matched.Id,EntityType=signal.Source.Replace("Microsoft ",""),EntityKey=signal.ExternalId,
                    RelationshipType="Assigns",Confidence=80,Authority=WorkThreadLinkAuthority.Deterministic,
                    LinkMethod="ExactProjectName",Status=WorkThreadLinkStatus.Candidate,SourceEventId=signal.SourceEventId
                });
                await signals.ResolveAsync(signal.Id,ContextSignalDisposition.Correlated,matched.Id,null,
                    "New external commitment correlated by exact project name; task import remains reviewable.");
            }
            else
                await signals.ResolveAsync(signal.Id,ContextSignalDisposition.NeedsReview,null,null,
                    "No unique deterministic Work Thread match; surfaced for review without creating a duplicate task.");

            var commitment=new GuardianNewCommitment
            {
                SignalId=signal.Id,Source=signal.Source,Title=signal.Title,Owner=signal.Owner,DueAt=signal.DueAt,
                WorkThreadId=matched?.Id,Reason=matched is null?"New assigned work with no confirmed local task or unique thread match.":
                    $"New assigned work correlated to {matched.Title}; no local task exists."
            };
            commitments.Add(commitment);
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId=signal.SourceEventId,CorrelationId=matched?.Id??signal.Id,OccurredAt=signal.ObservedAt,
                EventType="ExternalCommitmentDetected",Category="Context",Title=signal.Title,Narrative=commitment.Reason,
                ProjectId=matched?.PrimaryProjectId,ExternalId=$"{signal.Source}:{signal.ExternalId}",Outcome="NeedsReview",Significance=55
            });
        }
        workspace.NewCommitments=commitments.OrderBy(x=>x.DueAt??DateTimeOffset.MaxValue).ToList();
        workspace.WorkThreads=await threads.GetActiveAsync();
        return workspace.NewCommitments;
    }

    private static bool Equivalent(string left,string right)=>Normalize(left)==Normalize(right);
    private static string Normalize(string value)=>new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
