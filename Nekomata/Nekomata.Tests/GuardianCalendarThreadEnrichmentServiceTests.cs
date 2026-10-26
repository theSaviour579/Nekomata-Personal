using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianCalendarThreadEnrichmentServiceTests
{
    [Fact]
    public async Task Explicit_task_marker_confirms_meeting_and_attributes_without_missing_work()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-1", "Web Chat Change Control", "NEKOMATA:TASK:12");

        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        var context = Assert.Single(fixture.Workspace.CalendarThreadContexts);
        Assert.Equal(WorkThreadLinkStatus.Confirmed, context.LinkStatus);
        Assert.Equal(100, context.Confidence);
        Assert.Equal(12, context.TaskId);
        Assert.False(context.MissingRequiredWork);
        var effort = Assert.Single(fixture.Effort.Items);
        Assert.Equal(12, effort.TaskId);
        Assert.Equal("Planned", effort.State);
        Assert.Empty(fixture.Workspace.MissingWorkCandidates);
    }

    [Fact]
    public async Task Confirmed_project_meeting_without_task_flags_missing_required_work()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-2", "Web Chat Change Control", "NEKOMATA:PROJECT:7");

        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        Assert.True(Assert.Single(fixture.Workspace.CalendarThreadContexts).MissingRequiredWork);
        Assert.Equal("Confirmed Work Thread meeting has no associated task or external work item.",
            Assert.Single(fixture.Workspace.MissingWorkCandidates).Reason);
        Assert.Contains(fixture.Events.Items, x => x.EventType == "UnboundRequiredWorkDetected");
    }

    [Fact]
    public async Task Project_name_only_remains_candidate_and_does_not_allocate_effort()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-3", "Web Chat monthly update", "");

        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        var context = Assert.Single(fixture.Workspace.CalendarThreadContexts);
        Assert.Equal(WorkThreadLinkStatus.Candidate, context.LinkStatus);
        Assert.Equal(80, context.Confidence);
        Assert.Empty(fixture.Effort.Items);
    }

    [Fact]
    public async Task Partial_project_title_produces_ranked_candidate_for_manual_confirmation()
    {
        var fixture=new Fixture();
        fixture.Workspace.Projects[0].Name="Purchasing Review Program";
        fixture.Workspace.WorkThreads[0].Title="Purchasing Review Program";
        var meeting=Event("meeting-partial","Purchasing Review - Meeting","");

        await fixture.Service.EnrichAsync(fixture.Workspace,[meeting],DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        var context=Assert.Single(fixture.Workspace.CalendarThreadContexts);
        Assert.Equal(fixture.Workspace.WorkThreads[0].Id,context.ThreadId);
        Assert.Equal(WorkThreadLinkStatus.Candidate,context.LinkStatus);
        Assert.Equal(80,context.Confidence);
        Assert.Empty(fixture.Effort.Items);
    }

    [Fact]
    public async Task User_selected_thread_remains_confirmed_on_later_enrichment()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-4", "Purchasing Change Control", "");
        var threadId = Assert.Single(fixture.Workspace.WorkThreads).Id;

        await fixture.Service.ConfirmThreadAsync(meeting, threadId);
        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        var context = Assert.Single(fixture.Workspace.CalendarThreadContexts);
        Assert.Equal(threadId, context.ThreadId);
        Assert.Equal(WorkThreadLinkStatus.Confirmed, context.LinkStatus);
        Assert.Equal(100, context.Confidence);
        Assert.Equal("UserConfirmed", context.Explanation);
        Assert.Contains(fixture.Events.Items, x => x.EventType == "CalendarWorkThreadConfirmed" && x.UserDecision == "Approved");
        Assert.Single(fixture.Effort.Items);
    }

    [Fact]
    public async Task User_selected_task_is_retained_and_clears_missing_work()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-5", "Web Chat Change Control", "");
        var threadId = Assert.Single(fixture.Workspace.WorkThreads).Id;

        await fixture.Service.ConfirmThreadAsync(meeting, threadId);
        await fixture.Service.ConfirmTaskAsync(meeting, threadId, 12);
        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T09:00:00+01:00"));

        var context = Assert.Single(fixture.Workspace.CalendarThreadContexts);
        Assert.Equal(12, context.TaskId);
        Assert.False(context.MissingRequiredWork);
        Assert.Empty(fixture.Workspace.MissingWorkCandidates);
        Assert.Equal(12, Assert.Single(fixture.Effort.Items).TaskId);
        Assert.Contains(fixture.Events.Items, x => x.EventType == "CalendarTaskConfirmed" && x.TaskId == 12);
    }

    [Fact]
    public async Task Past_confirmed_meeting_records_actual_effort_transition()
    {
        var fixture = new Fixture();
        var meeting = Event("meeting-6", "Web Chat Change Control", "NEKOMATA:TASK:12");

        await fixture.Service.EnrichAsync(fixture.Workspace, [meeting], DateTimeOffset.Parse("2026-08-20T11:00:00+01:00"));

        Assert.Equal("Recorded", Assert.Single(fixture.Effort.Items).State);
        Assert.True(Assert.Single(fixture.Workspace.CalendarThreadContexts).IsRecorded);
        Assert.Contains(fixture.Events.Items, x => x.EventType == "CalendarEffortRecorded" && x.Outcome == "Recorded");
    }

    [Fact]
    public async Task Confirmed_meeting_with_expected_person_resolves_meeting_required_loop()
    {
        var loops = new FakeOpenLoops(new GuardianOpenLoop
        {
            Id=Guid.NewGuid(),Type=OpenLoopType.MeetingRequired,Status=OpenLoopStatus.Open,ProjectId=7,
            WaitingOn="Elaine Langley",Title="Arrange change control",Source="Teams",SourceEventId=Guid.NewGuid(),
            CreatedAt=DateTimeOffset.Parse("2026-08-19T09:00:00+01:00"),ExpectedEvidenceKind="MeetingScheduled"
        });
        var fixture = new Fixture(loops);
        var meeting = new CalendarEvent
        {
            Id="meeting-7",Subject="Web Chat Change Control",BodyPreview="NEKOMATA:PROJECT:7",
            Start=DateTimeOffset.Parse("2026-08-21T10:00:00+01:00"),End=DateTimeOffset.Parse("2026-08-21T10:30:00+01:00"),
            Attendees=["Elaine Langley"]
        };

        await fixture.Service.EnrichAsync(fixture.Workspace,[meeting],DateTimeOffset.Parse("2026-08-20T11:00:00+01:00"));

        Assert.Equal(1,Assert.Single(fixture.Workspace.CalendarThreadContexts).ResolvedOpenLoops);
        Assert.Equal(OpenLoopStatus.Resolved,Assert.Single(loops.Items).Status);
        Assert.Contains(fixture.Events.Items,x=>x.EventType=="OpenLoopResolved");
    }

    private static CalendarEvent Event(string id, string subject, string body) => new()
    {
        Id = id, Subject = subject, BodyPreview = body,
        Start = DateTimeOffset.Parse("2026-08-20T10:00:00+01:00"),
        End = DateTimeOffset.Parse("2026-08-20T10:30:00+01:00")
    };

    private sealed class Fixture
    {
        public FakeThreads Threads { get; } = new();
        public FakeEffort Effort { get; } = new();
        public FakeEvents Events { get; } = new();
        public NekomataWorkspace Workspace { get; }
        public GuardianCalendarThreadEnrichmentService Service { get; }

        public Fixture(FakeOpenLoops? loops=null)
        {
            var thread = new GuardianWorkThread
            {
                Id = Guid.NewGuid(), Title = "Web Chat", PrimaryProjectId = 7,
                Links =
                [
                    new GuardianWorkThreadLink { Id = Guid.NewGuid(), ThreadId = Guid.Empty, EntityType = "Project", EntityKey = "7", Status = WorkThreadLinkStatus.Confirmed },
                    new GuardianWorkThreadLink { Id = Guid.NewGuid(), ThreadId = Guid.Empty, EntityType = "Task", EntityKey = "12", Status = WorkThreadLinkStatus.Confirmed }
                ]
            };
            foreach (var link in thread.Links) link.ThreadId = thread.Id;
            Threads.Threads.Add(thread);
            Workspace = new NekomataWorkspace
            {
                Projects = [new NekomataProject { Id = 7, Name = "Web Chat" }],
                Tasks = [new NekomataTask { Id = 12, ProjectId = 7, Title = "Complete Web Chat change control" }],
                WorkThreads = [thread]
            };
            var journal=new WorkdayJournalService(Events);
            Service = new GuardianCalendarThreadEnrichmentService(Threads, Effort, journal, loops,
                loops is null?null:new GuardianOpenLoopService(loops,journal));
        }
    }

    private sealed class FakeThreads : IGuardianWorkThreadRepository
    {
        public List<GuardianWorkThread> Threads { get; } = [];
        public List<GuardianCorrelationEvidence> Evidence { get; } = [];
        public Task<GuardianWorkThread> GetOrCreateForProjectAsync(long id, string title, Guid source) => Task.FromResult(Threads.Single(x => x.PrimaryProjectId == id));
        public Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink link) { var existing = Threads.SelectMany(x => x.Links).FirstOrDefault(x => x.ThreadId == link.ThreadId && x.EntityType == link.EntityType && x.EntityKey == link.EntityKey); if (existing is not null) { existing.Confidence = Math.Max(existing.Confidence, link.Confidence); existing.Status = link.Status; existing.Authority = link.Authority; existing.LinkMethod = link.LinkMethod; return Task.FromResult(existing); } Threads.Single(x => x.Id == link.ThreadId).Links.Add(link); return Task.FromResult(link); }
        public Task AddEvidenceAsync(GuardianCorrelationEvidence item) { Evidence.Add(item); return Task.CompletedTask; }
        public Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid id) => Task.FromResult(Evidence.Where(x => x.LinkId == id).ToList());
        public Task UpdateEvaluationAsync(Guid id, int confidence, WorkThreadLinkStatus status, string method) { var link = Threads.SelectMany(x => x.Links).Single(x => x.Id == id); link.Confidence = confidence; link.Status = status; link.LinkMethod = method; return Task.CompletedTask; }
        public Task<GuardianWorkThreadLink?> FindLinkAsync(string type, string key, WorkThreadLinkStatus? status = null) => Task.FromResult(Threads.SelectMany(x => x.Links).FirstOrDefault(x => x.EntityType == type && x.EntityKey == key && (status is null || x.Status == status)));
        public Task<List<GuardianWorkThread>> GetActiveAsync() => Task.FromResult(Threads);
    }

    private sealed class FakeEffort : IGuardianCalendarEffortRepository
    {
        public List<GuardianCalendarEffort> Items { get; } = [];
        public Task UpsertAsync(GuardianCalendarEffort item) { Items.RemoveAll(x => x.CalendarEventId == item.CalendarEventId); Items.Add(item); return Task.CompletedTask; }
        public Task<List<GuardianCalendarEffort>> GetForThreadAsync(Guid id) => Task.FromResult(Items.Where(x => x.ThreadId == id).ToList());
    }

    private sealed class FakeOpenLoops(params GuardianOpenLoop[] loops):IGuardianOpenLoopRepository
    {
        public List<GuardianOpenLoop> Items{get;}=[..loops];
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop value){Items.Add(value);return Task.FromResult(value);}
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count=200)=>Task.FromResult(Items.Where(x=>x.Status==OpenLoopStatus.Open).ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count=200)=>GetOpenAsync(count);
        public Task<bool> ResolveAsync(Guid id,GuardianLoopEvidence evidence){var item=Items.Single(x=>x.Id==id);item.Status=OpenLoopStatus.Resolved;return Task.FromResult(true);}
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent item) { Items.Add(item); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }
}
