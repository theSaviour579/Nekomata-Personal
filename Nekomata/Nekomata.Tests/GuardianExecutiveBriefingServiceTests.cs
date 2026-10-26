using Nekomata.Core.Guardian.Briefing;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianExecutiveBriefingServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T09:00:00+01:00");

    [Fact]
    public async Task Preserves_the_supplied_offset_when_selecting_the_briefing_day()
    {
        var service = CreateService([], [], out var journal);

        await service.BuildAsync(new NekomataWorkspace(), [], Now);

        Assert.Equal(new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.FromHours(1)), journal.RequestedStart);
    }

    [Fact]
    public async Task Leads_with_decisions_and_explains_compliance_gates()
    {
        var workspace = new NekomataWorkspace
        {
            UnboundContext = [new() { EntityType = "CalendarEvent", EntityKey = "meeting-1", Title = "Choose a thread", Reason = "Correlation exists but requires confirmation." }],
            ProcessRequirements =
            [
                new() { Name = "Change Control", State = RequirementState.Blocked, Gate = ReadinessGate.Deployment },
                new() { Name = "ECSV", State = RequirementState.Required, Gate = ReadinessGate.Closure }
            ]
        };
        var service = CreateService([], [], out _);

        var result = await service.BuildAsync(workspace, [], Now);

        Assert.Equal(2, result.DecisionCount);
        Assert.True(result.Narrative.IndexOf("I need 2 decisions", StringComparison.Ordinal) <
                    result.Narrative.IndexOf("2 compliance requirements", StringComparison.Ordinal));
        Assert.Contains("1 affect deployment", result.ComplianceSummary);
        Assert.Contains("1 affect closure", result.ComplianceSummary);
        Assert.Contains("Choose a thread (CalendarEvent)", result.DecisionSummary);
    }

    [Fact]
    public async Task Compresses_routine_activity_and_reports_confirmed_calendar_context()
    {
        var journalEntry = new WorkdayEvent
        {
            OccurredAt = Now.AddMinutes(-10), EventType = "CalendarEffortAttributed", Outcome = "Attributed",
            Title = "Linked evidence"
        };
        var calendar = new CalendarEvent { Id = "meeting-1", Subject = "Purchasing Review", Start = Now.AddHours(1), End = Now.AddHours(1.5) };
        var workspace = new NekomataWorkspace
        {
            CalendarThreadContexts =
            [
                new() { CalendarEventId = "meeting-1", ThreadId = Guid.NewGuid(), LinkStatus = WorkThreadLinkStatus.Confirmed,
                    Explanation = "Explicit project link" }
            ]
        };
        var service = CreateService([], [journalEntry], out _);

        var result = await service.BuildAsync(workspace, [calendar], Now);

        Assert.Contains("completed 1 routine administrative action", result.HandledActivitySummary);
        Assert.Contains("1 is linked to confirmed work", result.ScheduleSummary);
        Assert.Contains(result.Provenance, x => x.SourceType == "Calendar" && x.Label == "Explicit project link");
    }

    [Fact]
    public async Task Authority_to_auto_execute_is_not_reported_as_an_action_that_happened()
    {
        var evaluation = new WorkdayEvent
        {
            OccurredAt = Now.AddMinutes(-10), EventType = "GuardianActionAuthorityEvaluated", Outcome = "AutoExecute",
            Title = "May attribute meeting effort"
        };
        var service = CreateService([], [evaluation], out _);

        var result = await service.BuildAsync(new NekomataWorkspace(), [], Now);

        Assert.Contains("No autonomous administrative changes", result.HandledActivitySummary);
    }

    [Fact]
    public async Task Keeps_prepared_follow_up_distinct_from_sent_or_cooling_down()
    {
        var loop = new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Type = OpenLoopType.AwaitingThem, Status = OpenLoopStatus.Open,
            CreatedAt = Now.AddDays(-4), WaitingOn = "Phil", Title = "Purchasing Review", SourceEventId = Guid.NewGuid()
        };
        var attempt = new GuardianChaseAttempt
        {
            OpenLoopId = loop.Id, AttemptNumber = 1, Status = GuardianChaseStatus.Prepared, PreparedAt = Now.AddHours(-1)
        };
        var service = CreateService([loop], [], out _, [attempt]);

        var result = await service.BuildAsync(new NekomataWorkspace(), [], Now);

        Assert.Contains("1 follow-up is prepared for approval", result.PreparedActionSummary);
        Assert.Equal(1, result.DecisionCount);
        Assert.Contains("1 action awaiting approval", result.DecisionSummary);
        Assert.DoesNotContain("cooling down", result.PreparedActionSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sent", result.PreparedActionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Does_not_turn_the_unmatched_integration_backlog_into_decisions()
    {
        var workspace = new NekomataWorkspace
        {
            UnboundContext = Enumerable.Range(1, 199).Select(x => new GuardianUnboundContext
            {
                EntityType = "PlannerTask", EntityKey = x.ToString(), Title = $"Imported task {x}",
                Reason = "No correlation evidence found."
            }).ToList()
        };
        var service = CreateService([], [], out _);

        var result = await service.BuildAsync(workspace, [], Now);

        Assert.Equal(0, result.DecisionCount);
        Assert.Contains("don't currently need a decision", result.DecisionSummary);
    }

    [Fact]
    public async Task Missing_work_is_not_misreported_as_an_ambiguous_context_decision()
    {
        var workspace = new NekomataWorkspace
        {
            MissingWorkCandidates = [new() { SourceType = "CalendarEvent", SourceKey = "meeting-1", Title = "Change Control" }]
        };
        var service = CreateService([], [], out _);

        var result = await service.BuildAsync(workspace, [], Now);

        Assert.Equal(0, result.DecisionCount);
    }

    private static GuardianExecutiveBriefingService CreateService(
        GuardianOpenLoop[] loops, WorkdayEvent[] entries, out FakeEvents events, GuardianChaseAttempt[]? attempts = null)
    {
        events = new FakeEvents(entries);
        var age = new GuardianOpenLoopAgeService(new FakeOpenLoops(loops), new WorkdayJournalService(events), new FakeChases(attempts ?? []));
        return new GuardianExecutiveBriefingService(age, events);
    }

    private sealed class FakeOpenLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop value) => Task.FromResult(value);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeChases(params GuardianChaseAttempt[] attempts) : IGuardianChaseRepository
    {
        public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid id) => Task.FromResult(attempts.FirstOrDefault(x => x.OpenLoopId == id && x.Status == GuardianChaseStatus.Prepared));
        public Task<int> GetAttemptCountAsync(Guid id) => Task.FromResult(attempts.Count(x => x.OpenLoopId == id));
        public Task AddAsync(GuardianChaseAttempt value) => Task.CompletedTask;
        public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) => Task.FromResult(attempts.Where(x => x.Status == status).ToList());
        public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid id) => Task.FromResult(attempts.Where(x => x.OpenLoopId == id).ToList());
        public Task<bool> ApproveAsync(Guid id, DateTimeOffset at) => Task.FromResult(false);
        public Task RecordSendFailureAsync(Guid id, DateTimeOffset at, string error) => Task.CompletedTask;
        public Task<bool> MarkSentAsync(Guid id, DateTimeOffset at, DateTimeOffset due) => Task.FromResult(false);
    }

    private sealed class FakeEvents(params WorkdayEvent[] entries) : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [.. entries];
        public DateTimeOffset? RequestedStart { get; private set; }
        public Task AddAsync(WorkdayEvent value) { Items.Add(value); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500)
        {
            RequestedStart = from;
            return Task.FromResult(Items.Where(x => x.OccurredAt >= from && x.OccurredAt < to).ToList());
        }
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }
}
