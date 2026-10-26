using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Core.Guardian.Authority;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class AnticipatoryOperationsEngineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-21T08:00:00+01:00");

    [Fact]
    public async Task Confirmed_upcoming_meeting_queues_preparation_with_provenance()
    {
        var threadId = Guid.NewGuid();
        var workspace = new NekomataWorkspace
        {
            CalendarThreadContexts = [new() { CalendarEventId = "meeting-1", ThreadId = threadId, LinkStatus = WorkThreadLinkStatus.Confirmed }],
            WorkThreadSummaries = [new() { ThreadId = threadId, Title = "Purchasing Review", PrimaryProject = "Purchasing Review Program",
                HighestImpactDependency = "Waiting on Phil", ActiveTasks = 4, OpenLoops = 1,
                RecentChanges = ["Change Control task created"], TeamsConversations = ["Craig confirmed tomorrow's implementation window"] }]
        };
        var meeting = new CalendarEvent { Id = "meeting-1", Subject = "Purchasing change control", Start = Now.AddHours(2), End = Now.AddHours(2.5), Attendees = ["Craig Cranfield"] };
        var events = new FakeEvents();

        var result = await Engine(events).EvaluateAsync(workspace, [meeting], Now);

        var action = Assert.Single(result);
        Assert.Equal("PrepareMeetingContext", action.ProposedAction);
        Assert.Equal(GuardianActionAuthority.Prepare, action.Authority);
        Assert.Equal(AnticipatedActionStatus.Prepared, action.Status);
        Assert.Contains(action.Evidence, x => x.Contains("Waiting on Phil"));
        Assert.Contains("MEETING PREPARATION · Purchasing change control", action.PreparedContent);
        Assert.Contains("Craig Cranfield", action.PreparedContent);
        Assert.Contains("Waiting on Phil", action.PreparedContent);
        Assert.Contains("Craig confirmed tomorrow's implementation window", action.PreparedContent);
        Assert.Contains(events.Items, x => x.EventType == "AnticipatedActionQueued" && x.ExternalId == action.Id.ToString("N"));
        Assert.Contains(events.Items, x => x.EventType == "MeetingContextPrepared");
    }

    [Fact]
    public async Task Outstanding_requirement_adds_separate_readiness_check()
    {
        var threadId = Guid.NewGuid();
        var workspace = new NekomataWorkspace
        {
            CalendarThreadContexts = [new() { CalendarEventId = "meeting-1", ThreadId = threadId, LinkStatus = WorkThreadLinkStatus.Confirmed }],
            ProcessRequirements = [new() { WorkThreadId = threadId, Name = "Change Control", State = RequirementState.Required, Gate = ReadinessGate.Deployment }]
        };
        var meeting = new CalendarEvent { Id = "meeting-1", Subject = "Implementation", Start = Now.AddHours(3), End = Now.AddHours(4) };

        var result = await Engine(new FakeEvents()).EvaluateAsync(workspace, [meeting], Now);

        Assert.Contains(result, x => x.ProposedAction == "CheckReadinessGate" && x.Evidence.Any(e => e.Contains("Deployment gate")));
        Assert.Equal(2, result.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task Nekomata_managed_focus_block_never_generates_meeting_preparation_or_readiness_action()
    {
        var threadId = Guid.NewGuid();
        var workspace = new NekomataWorkspace
        {
            CalendarThreadContexts = [new() { CalendarEventId = "focus-1", ThreadId = threadId, LinkStatus = WorkThreadLinkStatus.Confirmed }],
            WorkThreadSummaries = [new() { ThreadId = threadId, Title = "Purchasing Review" }],
            ProcessRequirements = [new() { WorkThreadId = threadId, Name = "Change Control", State = RequirementState.Required }]
        };
        var focus = new CalendarEvent
        {
            Id = "focus-1", Subject = "Focus · Purchasing Review", Start = Now.AddHours(2), End = Now.AddHours(3),
            BodyPreview = "NEKOMATA:task:74"
        };

        var result = await Engine(new FakeEvents()).EvaluateAsync(workspace, [focus], Now);

        Assert.DoesNotContain(result, x => x.TriggerKey == focus.Id &&
            x.ProposedAction is "PrepareMeetingContext" or "CheckReadinessGate");
    }

    [Fact]
    public async Task Teams_meeting_request_selects_verified_free_slot_and_prepares_reply()
    {
        var threadId = Guid.NewGuid();
        var signal = new ContextSignal
        {
            Id = Guid.NewGuid(), Source = "Microsoft Teams", Kind = ContextSignalKind.Conversation,
            Person = "Craig Cranfield", Body = "Have you got half an hour tomorrow morning to discuss this?",
            Title = "Have you got half an hour tomorrow morning to discuss this?", ObservedAt = Now,
            WorkThreadId = threadId, ConversationId = "chat-123"
        };
        var tomorrow = Now.AddDays(1).Date;
        var offset = TimeZoneInfo.Local.GetUtcOffset(tomorrow);
        var calendar = new[]
        {
            new CalendarEvent { Id = "busy", Subject = "Busy", Start = new DateTimeOffset(tomorrow.AddHours(8), offset), End = new DateTimeOffset(tomorrow.AddHours(9.5), offset) }
        };
        var workspace = new NekomataWorkspace { WorkThreadSummaries = [new() { ThreadId = threadId, Title = "Purchasing Review" }] };
        var events = new FakeEvents();
        var journal = new WorkdayJournalService(events);
        var age = new GuardianOpenLoopAgeService(new FakeLoops(), journal, new FakeChases());
        var engine = new AnticipatoryOperationsEngine(new GuardianActionAuthorityEngine(journal), age,
            new OperationalIntentDetectionService(new FakeSignals(signal)), journal);

        var result = await engine.EvaluateAsync(workspace, calendar, Now);

        var action = Assert.Single(result);
        Assert.Equal(AnticipatedActionStatus.Prepared, action.Status);
        Assert.Equal(9, action.SuggestedStart!.Value.Hour);
        Assert.Equal(30, action.SuggestedStart.Value.Minute);
        Assert.Contains("Hi Craig", action.PreparedContent);
        Assert.Contains("Purchasing Review", action.PreparedContent);
        Assert.Equal("chat-123", action.TargetConversationId);
    }

    private static AnticipatoryOperationsEngine Engine(FakeEvents events)
    {
        var journal = new WorkdayJournalService(events);
        var age = new GuardianOpenLoopAgeService(new FakeLoops(), journal, new FakeChases());
        return new AnticipatoryOperationsEngine(new GuardianActionAuthorityEngine(journal), age,
            new OperationalIntentDetectionService(new FakeSignals()), journal);
    }

    private sealed class FakeLoops : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop value) => Task.FromResult(value);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(new List<GuardianOpenLoop>());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(new List<GuardianOpenLoop>());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeChases : IGuardianChaseRepository
    {
        public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid id) => Task.FromResult<GuardianChaseAttempt?>(null);
        public Task<int> GetAttemptCountAsync(Guid id) => Task.FromResult(0);
        public Task AddAsync(GuardianChaseAttempt value) => Task.CompletedTask;
        public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) => Task.FromResult(new List<GuardianChaseAttempt>());
        public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid id) => Task.FromResult(new List<GuardianChaseAttempt>());
        public Task<bool> ApproveAsync(Guid id, DateTimeOffset at) => Task.FromResult(false);
        public Task RecordSendFailureAsync(Guid id, DateTimeOffset at, string error) => Task.CompletedTask;
        public Task<bool> MarkSentAsync(Guid id, DateTimeOffset at, DateTimeOffset due) => Task.FromResult(false);
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent value) { Items.Add(value); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }

    private sealed class FakeSignals(params ContextSignal[] values) : IContextSignalRepository
    {
        public Task<ContextSignal> UpsertAsync(ContextSignal value) => Task.FromResult(value);
        public Task<List<ContextSignal>> GetCurrentAsync(int count = 1000) => Task.FromResult(values.ToList());
        public Task ResolveAsync(Guid id, ContextSignalDisposition disposition, Guid? threadId, long? taskId, string resolution) => Task.CompletedTask;
    }
}
