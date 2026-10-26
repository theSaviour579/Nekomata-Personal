using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Context;

public sealed class TeamsContextEvidenceService(
    IContextSignalRepository signals,
    IGuardianWorkThreadRepository threads,
    WorkdayJournalService journal,
    ProjectCommunicationLoopService? communicationLoops = null)
{
    private static readonly HashSet<string> StopWords=new(StringComparer.OrdinalIgnoreCase)
    {"project","review","change","control","meeting","discussion","task","work","with","from","that","this","the","and","for",
     "complete","completed","completion","document","documentation","approval","required","prepare","update","request","action"};

    public async Task CorrelateAsync(NekomataWorkspace workspace,DateTimeOffset now)
    {
        var current=await signals.GetCurrentAsync();
        var conversations=current.Where(x=>x.Source=="Microsoft Teams"&&x.Kind==ContextSignalKind.Conversation).ToList();
        var meetings=current.Where(x=>x.Source=="Microsoft Calendar"&&x.Kind==ContextSignalKind.Meeting).ToList();
        foreach(var message in conversations)
            await CorrelateMessageAsync(workspace,message,conversations,meetings,now);

        if (communicationLoops is not null)
            await communicationLoops.AnalyseTeamsAsync(workspace, conversations);

        workspace.WorkThreads=await threads.GetActiveAsync();
        foreach(var meeting in meetings)
            await StrengthenMeetingAsync(workspace,meeting,conversations,now);
        workspace.WorkThreads=await threads.GetActiveAsync();
    }

    private async Task CorrelateMessageAsync(NekomataWorkspace workspace,ContextSignal message,
        IReadOnlyCollection<ContextSignal> conversations,IReadOnlyCollection<ContextSignal> meetings,DateTimeOffset now)
    {
        // Only the message itself can establish relevance. Surrounding messages are display context,
        // not evidence, so one keyword cannot contaminate an otherwise unrelated conversation.
        var text=$"{message.Title} {message.Body}";
        var messageTerms=Terms(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        GuardianCorrelationEvaluation? best=null;GuardianWorkThread? bestThread=null;
        foreach(var thread in workspace.WorkThreads)
        {
            var project=thread.PrimaryProjectId is long id?workspace.Projects.FirstOrDefault(x=>x.Id==id):null;
            if(project is null) continue;
            var evidence=new List<(string Type,string Value,int Weight)>();
            var hasContentAnchor=false;
            var hasStrongContentAnchor=false;
            var projectTerms=Terms(project.Name);
            var matchedProjectTerms=projectTerms.Where(messageTerms.Contains).ToList();
            if(projectTerms.Count>=2&&matchedProjectTerms.Count==projectTerms.Count)
            {
                evidence.Add(("ExactProjectName",project.Name,70));
                hasContentAnchor=true;
                hasStrongContentAnchor=true;
            }
            else
            {
                if(matchedProjectTerms.Count>0)
                {
                    evidence.Add(("DistinctiveProjectTerms",string.Join(", ",matchedProjectTerms),45));
                    hasContentAnchor=true;
                    hasStrongContentAnchor=matchedProjectTerms.Count>=2;
                }
            }
            var matchingTaskTerms=workspace.Tasks.Where(x=>x.ProjectId==project.Id)
                .Select(x=>Terms(x.Title))
                .Select(terms=>terms.Where(messageTerms.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                .Where(matches=>matches.Count>=2).OrderByDescending(matches=>matches.Count).FirstOrDefault();
            if(matchingTaskTerms is not null)
            {
                evidence.Add(("TaskTerminology",string.Join(", ",matchingTaskTerms.Take(4)),30));
                hasContentAnchor=true;
                hasStrongContentAnchor=true;
            }
            if(!hasContentAnchor)
            {
                var unsupported=thread.Links.FirstOrDefault(x=>x.EntityType=="TeamsMessage"&&x.EntityKey==message.ExternalId);
                if(unsupported is not null)
                    await threads.UpdateEvaluationAsync(unsupported.Id,0,WorkThreadLinkStatus.Rejected,"NoSubstantiveContentAnchor");
                continue;
            }
            if(hasStrongContentAnchor)
            {
                if(conversations.Any(x=>x.Id!=message.Id&&x.ConversationId==message.ConversationId&&x.WorkThreadId==thread.Id))
                    evidence.Add(("ConversationContinuity",message.ConversationId,25));
                if(meetings.Any(x=>Math.Abs((x.ObservedAt-message.ObservedAt).TotalDays)<=3&&
                    x.Body.Contains(message.Person,StringComparison.OrdinalIgnoreCase)&&Terms(x.Title).Intersect(Terms(text),StringComparer.OrdinalIgnoreCase).Any()))
                    evidence.Add(("MeetingAttendeeProximity",message.Person,25));
            }
            if(evidence.Count==0)continue;
            var link=await threads.UpsertLinkAsync(new GuardianWorkThreadLink
            {
                ThreadId=thread.Id,EntityType="TeamsMessage",EntityKey=message.ExternalId,RelationshipType="Discusses",
                Confidence=0,Authority=WorkThreadLinkAuthority.Deterministic,LinkMethod="TeamsContextEvidence",
                Status=WorkThreadLinkStatus.Candidate,SourceEventId=message.SourceEventId
            });
            await threads.ClearEvidenceAsync(link.Id);
            foreach(var item in evidence)await threads.AddEvidenceAsync(new GuardianCorrelationEvidence
            {LinkId=link.Id,EvidenceType=item.Type,Value=item.Value,Weight=item.Weight,SourceEventId=message.SourceEventId});
            var evaluation=GuardianCorrelationPolicy.Evaluate(await threads.GetEvidenceAsync(link.Id));
            await threads.UpdateEvaluationAsync(link.Id,evaluation.Confidence,evaluation.Status,evaluation.Explanation);
            if(best is null||evaluation.Confidence>best.Confidence){best=evaluation;bestThread=thread;}
        }
        if(best is null)
        {
            if(message.Disposition==ContextSignalDisposition.Correlated||message.WorkThreadId is not null)
                await signals.ResolveAsync(message.Id,ContextSignalDisposition.Superseded,null,null,
                    "Teams context rejected: no substantive project or task content anchor.");
            return;
        }
        message.Disposition=best.Status==WorkThreadLinkStatus.Confirmed?ContextSignalDisposition.Correlated:ContextSignalDisposition.NeedsReview;
        message.WorkThreadId=bestThread?.Id;
        await signals.ResolveAsync(message.Id,message.Disposition,
            bestThread?.Id,null,$"Teams context from {Sender(message)}: {best.Explanation} = {best.Confidence}%.");
        if(best.Status==WorkThreadLinkStatus.Confirmed)
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId=message.SourceEventId,CorrelationId=bestThread?.Id??message.Id,OccurredAt=message.ObservedAt,
                EventType="TeamsContextCorrelated",Category="Context",Title=$"Teams context from {Sender(message)} confirmed",
                Narrative=$"Confirmed Teams relationship from {Sender(message)} to {bestThread?.Title}: {best.Explanation} ({best.Confidence}%).",
                ProjectId=bestThread?.PrimaryProjectId,ExternalId=$"Teams:{message.ExternalId}",Outcome=best.Status.ToString(),Significance=50
            });
        else
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId=message.SourceEventId,CorrelationId=bestThread?.Id??message.Id,OccurredAt=message.ObservedAt,
                EventType="TeamsContextReviewRequired",Category="Context",
                Title=$"Review Teams message from {Sender(message)}: {Excerpt(message.Title)}",
                Narrative=$"Possible relationship to {bestThread?.Title} from {Sender(message)}: {best.Explanation} ({best.Confidence}%).",
                ProjectId=bestThread?.PrimaryProjectId,ExternalId=$"TeamsReview:{message.ExternalId}",Outcome="NeedsReview",Significance=45
            });
    }

    private async Task StrengthenMeetingAsync(NekomataWorkspace workspace,ContextSignal meeting,
        IReadOnlyCollection<ContextSignal> conversations,DateTimeOffset now)
    {
        foreach(var group in conversations.Where(x=>x.Disposition==ContextSignalDisposition.Correlated&&x.WorkThreadId is not null&&
                    Math.Abs((meeting.ObservedAt-x.ObservedAt).TotalDays)<=3).GroupBy(x=>x.WorkThreadId!.Value))
        {
            var thread=workspace.WorkThreads.FirstOrDefault(x=>x.Id==group.Key);if(thread is null)continue;
            var shared=group.SelectMany(x=>Terms($"{x.Title} {x.Body}")).Intersect(Terms(meeting.Title),StringComparer.OrdinalIgnoreCase).ToList();
            var attendee=group.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x.Person)&&meeting.Body.Contains(x.Person,StringComparison.OrdinalIgnoreCase));
            if(shared.Count==0&&attendee is null)continue;
            var link=await threads.UpsertLinkAsync(new GuardianWorkThreadLink
            {
                ThreadId=thread.Id,EntityType="CalendarEvent",EntityKey=meeting.ExternalId,RelationshipType="Schedules",
                Confidence=0,Authority=WorkThreadLinkAuthority.Deterministic,LinkMethod="TeamsContext",
                Status=WorkThreadLinkStatus.Candidate,SourceEventId=meeting.SourceEventId
            });
            var evidence=new List<(string Type,string Value,int Weight)>{("ConfirmedTeamsThread",group.Key.ToString("N"),60),("TemporalProximity","within 3 days",15)};
            if(shared.Count>0)evidence.Add(("SharedTerminology",string.Join(", ",shared),20));
            if(attendee is not null)evidence.Add(("MatchingAttendee",attendee.Person,20));
            foreach(var item in evidence)await threads.AddEvidenceAsync(new GuardianCorrelationEvidence
            {LinkId=link.Id,EvidenceType=item.Type,Value=item.Value,Weight=item.Weight,SourceEventId=meeting.SourceEventId});
            var evaluation=GuardianCorrelationPolicy.Evaluate(await threads.GetEvidenceAsync(link.Id));
            await threads.UpdateEvaluationAsync(link.Id,evaluation.Confidence,evaluation.Status,evaluation.Explanation);
            if(evaluation.Status==WorkThreadLinkStatus.Confirmed)
                await signals.ResolveAsync(meeting.Id,ContextSignalDisposition.Correlated,thread.Id,null,
                    $"Meeting confirmed from Teams context: {evaluation.Explanation} ({evaluation.Confidence}%).");
        }
    }

    private static List<string> Terms(string value)=>System.Text.RegularExpressions.Regex.Matches(value,"[A-Za-z0-9]+")
        .Select(x=>x.Value).Where(x=>x.Length>=4&&!StopWords.Contains(x))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Sender(ContextSignal message)=>string.IsNullOrWhiteSpace(message.Person)?"Unknown sender":message.Person;
    private static string Excerpt(string value)=>value.Length<=90?value:$"{value[..87]}...";
}
