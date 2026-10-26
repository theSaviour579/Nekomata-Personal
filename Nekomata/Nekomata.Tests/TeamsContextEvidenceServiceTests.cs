using Nekomata.Core.Guardian.Context;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class TeamsContextEvidenceServiceTests
{
    [Fact]
    public async Task Independent_teams_evidence_confirms_thread_and_strengthens_related_meeting()
    {
        var now=DateTimeOffset.Parse("2026-08-20T09:00:00+01:00");
        var message=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m1","Purchasing Review database handover","Craig",now,"chat-1");
        var meeting=Signal("Microsoft Calendar",ContextSignalKind.Meeting,"c1","Purchasing Change Control","Craig",now.AddDays(1));
        var signals=new FakeSignals(message,meeting);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace
        {
            Projects=[new NekomataProject { Id=7,Name="Purchasing Review" }],WorkThreads=[threads.Thread],
            Tasks=[new NekomataTask { Id=12,ProjectId=7,Title="Prepare purchasing database handover" }]
        };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,now);

        var teamsLink=Assert.Single(threads.Thread.Links,x=>x.EntityType=="TeamsMessage");
        Assert.Equal(WorkThreadLinkStatus.Confirmed,teamsLink.Status);
        Assert.Equal(100,teamsLink.Confidence);
        var calendarLink=Assert.Single(threads.Thread.Links,x=>x.EntityType=="CalendarEvent");
        Assert.Equal(WorkThreadLinkStatus.Confirmed,calendarLink.Status);
        Assert.Equal(ContextSignalDisposition.Correlated,meeting.Disposition);
        Assert.Contains(events.Items,x=>x.EventType=="TeamsContextCorrelated"&&x.Outcome=="Confirmed");
    }

    [Fact]
    public async Task Project_name_alone_remains_candidate_for_review()
    {
        var message=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m2","Purchasing Review update","Craig",DateTimeOffset.UtcNow,"chat-2");
        var signals=new FakeSignals(message);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace { Projects=[new NekomataProject { Id=7,Name="Purchasing Review" }],WorkThreads=[threads.Thread] };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,DateTimeOffset.UtcNow);

        var link=Assert.Single(threads.Thread.Links);
        Assert.Equal(45,link.Confidence);
        Assert.Equal(WorkThreadLinkStatus.Candidate,link.Status);
        Assert.Equal(ContextSignalDisposition.NeedsReview,message.Disposition);
        Assert.Contains(events.Items,x=>x.EventType=="TeamsContextReviewRequired"&&x.Title.Contains("Craig"));
    }

    [Fact]
    public async Task Single_distinctive_project_keyword_creates_sender_identified_review_candidate()
    {
        var message=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m4","Can we discuss Purchasing tomorrow?","Emma",DateTimeOffset.UtcNow,"chat-4");
        var signals=new FakeSignals(message);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace { Projects=[new NekomataProject { Id=7,Name="Purchasing Review Program" }],WorkThreads=[threads.Thread] };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,DateTimeOffset.UtcNow);

        var link=Assert.Single(threads.Thread.Links);
        Assert.Equal(45,link.Confidence);
        Assert.Equal(WorkThreadLinkStatus.Candidate,link.Status);
        Assert.Equal(ContextSignalDisposition.NeedsReview,message.Disposition);
        Assert.Contains("Emma",message.Resolution);
        Assert.Contains(events.Items,x=>x.Title.Contains("Review Teams message from Emma")&&x.Title.Contains("Purchasing"));
    }

    [Fact]
    public async Task Only_keyword_message_is_correlated_while_neighbours_remain_display_context()
    {
        var now=DateTimeOffset.Parse("2026-08-20T14:17:00+01:00");
        var request=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m5","Can we do this change control?","Elaine Langley",now,"chat-5");
        var clarification=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m6","Purchasing","Elaine Langley",now.AddMinutes(12),"chat-5");
        var arrangement=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m7","We can do first thing tomorrow","Elaine Langley",now.AddMinutes(13),"chat-5");
        var signals=new FakeSignals(request,clarification,arrangement);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace { Projects=[new NekomataProject { Id=7,Name="Purchasing Review Program" }],WorkThreads=[threads.Thread] };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,now);

        var link=Assert.Single(threads.Thread.Links,x=>x.EntityType=="TeamsMessage"&&x.Status==WorkThreadLinkStatus.Candidate);
        Assert.Equal("m6",link.EntityKey);
        Assert.Equal(ContextSignalDisposition.NeedsReview,clarification.Disposition);
        Assert.Equal(threads.Thread.Id,clarification.WorkThreadId);
        Assert.Equal(ContextSignalDisposition.New,request.Disposition);
        Assert.Equal(ContextSignalDisposition.New,arrangement.Disposition);
    }

    [Fact]
    public async Task Unrelated_chat_does_not_correlate_even_with_attendee_and_conversation_support()
    {
        var now=DateTimeOffset.Parse("2026-08-20T09:00:00+01:00");
        var unrelated=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m3","Back now - enjoy your carplay","Craig",now,"chat-3");
        unrelated.Disposition=ContextSignalDisposition.Correlated;
        var meeting=Signal("Microsoft Calendar",ContextSignalKind.Meeting,"c3","Purchasing Change Control","Craig",now.AddDays(1));
        var signals=new FakeSignals(unrelated,meeting);var threads=new FakeThreads();var events=new FakeEvents();
        threads.Thread.Links.Add(new GuardianWorkThreadLink
        {
            ThreadId=threads.Thread.Id,EntityType="TeamsMessage",EntityKey="m3",RelationshipType="Discusses",
            Confidence=95,Authority=WorkThreadLinkAuthority.Deterministic,Status=WorkThreadLinkStatus.Confirmed
        });
        unrelated.WorkThreadId=threads.Thread.Id;
        var workspace=new NekomataWorkspace
        {
            Projects=[new NekomataProject { Id=7,Name="Purchasing Review Program" }],WorkThreads=[threads.Thread],
            Tasks=[new NekomataTask { Id=12,ProjectId=7,Title="Complete purchasing database handover" }]
        };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,now);

        var link=Assert.Single(threads.Thread.Links,x=>x.EntityType=="TeamsMessage");
        Assert.Equal(WorkThreadLinkStatus.Rejected,link.Status);
        Assert.Equal(0,link.Confidence);
        Assert.Equal(ContextSignalDisposition.Superseded,unrelated.Disposition);
        Assert.Null(unrelated.WorkThreadId);
        Assert.DoesNotContain(threads.Thread.Links,x=>x.EntityType=="CalendarEvent");
        Assert.DoesNotContain(events.Items,x=>x.EventType=="TeamsContextCorrelated");
    }

    [Fact]
    public async Task Generic_task_words_and_partial_word_matches_do_not_create_purchasing_context()
    {
        var now=DateTimeOffset.Parse("2026-08-20T09:00:00+01:00");
        var process=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m8",
            "We are getting the outstanding change control completed","Craig",now,"chat-8");
        var customer=Signal("Microsoft Teams",ContextSignalKind.Conversation,"m9",
            "A customer who has not purchased in the preceding twelve months","Barry",now.AddMinutes(1),"chat-9");
        var signals=new FakeSignals(process,customer);var threads=new FakeThreads();var events=new FakeEvents();
        var workspace=new NekomataWorkspace
        {
            Projects=[new NekomataProject { Id=7,Name="Purchasing Review Program" }],WorkThreads=[threads.Thread],
            Tasks=[new NekomataTask { Id=12,ProjectId=7,Title="Complete change control documentation" }]
        };

        await new TeamsContextEvidenceService(signals,threads,new WorkdayJournalService(events)).CorrelateAsync(workspace,now);

        Assert.Empty(threads.Thread.Links);
        Assert.All(new[]{process,customer},x=>Assert.Null(x.WorkThreadId));
    }

    private static ContextSignal Signal(string source,ContextSignalKind kind,string id,string text,string person,DateTimeOffset observed,string conversation="")=>new()
    { Source=source,Kind=kind,ExternalId=id,Title=text,Body=person,Person=person,ObservedAt=observed,ConversationId=conversation,SourceEventId=Guid.NewGuid() };

    private sealed class FakeSignals(params ContextSignal[] values):IContextSignalRepository
    {
        private readonly List<ContextSignal> _items=[..values];
        public Task<ContextSignal> UpsertAsync(ContextSignal value){_items.Add(value);return Task.FromResult(value);}
        public Task<List<ContextSignal>> GetCurrentAsync(int count=1000)=>Task.FromResult(_items.Take(count).ToList());
        public Task ResolveAsync(Guid id,ContextSignalDisposition disposition,Guid? thread,long? task,string resolution){var x=_items.Single(y=>y.Id==id);x.Disposition=disposition;x.WorkThreadId=thread;x.ExistingTaskId=task;x.Resolution=resolution;return Task.CompletedTask;}
    }
    private sealed class FakeThreads:IGuardianWorkThreadRepository
    {
        public GuardianWorkThread Thread{get;}=new(){Id=Guid.NewGuid(),Title="Purchasing Review",PrimaryProjectId=7};
        private readonly List<GuardianCorrelationEvidence> _evidence=[];
        public Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink value){var old=Thread.Links.FirstOrDefault(x=>x.EntityType==value.EntityType&&x.EntityKey==value.EntityKey);if(old is not null)return Task.FromResult(old);Thread.Links.Add(value);return Task.FromResult(value);}
        public Task AddEvidenceAsync(GuardianCorrelationEvidence value){if(!_evidence.Any(x=>x.LinkId==value.LinkId&&x.EvidenceType==value.EvidenceType&&x.Value==value.Value))_evidence.Add(value);return Task.CompletedTask;}
        public Task ClearEvidenceAsync(Guid id){_evidence.RemoveAll(x=>x.LinkId==id);return Task.CompletedTask;}
        public Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid id)=>Task.FromResult(_evidence.Where(x=>x.LinkId==id).ToList());
        public Task UpdateEvaluationAsync(Guid id,int confidence,WorkThreadLinkStatus status,string method){var link=Thread.Links.Single(x=>x.Id==id);link.Confidence=confidence;link.Status=status;link.LinkMethod=method;return Task.CompletedTask;}
        public Task<List<GuardianWorkThread>> GetActiveAsync()=>Task.FromResult(new List<GuardianWorkThread>{Thread});
        public Task<GuardianWorkThread> GetOrCreateForProjectAsync(long id,string title,Guid source)=>Task.FromResult(Thread);
        public Task<GuardianWorkThreadLink?> FindLinkAsync(string type,string key,WorkThreadLinkStatus? status=null)=>Task.FromResult(Thread.Links.FirstOrDefault(x=>x.EntityType==type&&x.EntityKey==key));
    }
    private sealed class FakeEvents:IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items{get;}=[];public Task AddAsync(WorkdayEvent x){Items.Add(x);return Task.CompletedTask;}
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from,DateTimeOffset to,int count=500)=>Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query,int count=100)=>Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type,string id)=>Task.FromResult(Items.Any(x=>x.EventType==type&&x.ExternalId==id));
    }
}
