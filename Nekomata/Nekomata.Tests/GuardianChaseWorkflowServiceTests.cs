using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianChaseWorkflowServiceTests
{
    [Fact]
    public async Task Approves_then_marks_sent_only_after_transport_succeeds_and_starts_cooldown()
    {
        var attempt = Prepared();
        var repository = new FakeChases(attempt);
        var email = new FakeEmail();
        var events = new FakeEvents();
        var service = new GuardianChaseWorkflowService(repository, email, new WorkdayJournalService(events));
        var friday = DateTimeOffset.Parse("2026-08-21T10:00:00+01:00");

        await service.ApprovePreparedAsync(friday);
        Assert.Equal(GuardianChaseStatus.Approved, attempt.Status);
        Assert.Null(attempt.SentAt);

        var result = Assert.Single(await service.SendApprovedAsync(friday));

        Assert.True(result.Sent);
        Assert.Equal(GuardianChaseStatus.Sent, attempt.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-08-26T10:00:00+01:00"), attempt.NextChaseDueAt);
        Assert.Single(email.Sent);
        Assert.Contains(events.Items, x => x.EventType == "OpenLoopChaseApproved");
        Assert.Contains(events.Items, x => x.EventType == "OpenLoopChaseSent");
        Assert.Contains(events.Items, x => x.EventType == "OpenLoopChaseCooldownStarted");
    }

    [Fact]
    public async Task Send_failure_preserves_approved_state_and_records_error()
    {
        var attempt = Prepared();
        attempt.Status = GuardianChaseStatus.Approved;
        var repository = new FakeChases(attempt);
        var email = new FakeEmail { Failure = new HttpRequestException("Graph unavailable") };
        var events = new FakeEvents();
        var service = new GuardianChaseWorkflowService(repository, email, new WorkdayJournalService(events));

        var result = Assert.Single(await service.SendApprovedAsync(DateTimeOffset.UtcNow));

        Assert.False(result.Sent);
        Assert.Equal(GuardianChaseStatus.Approved, attempt.Status);
        Assert.Equal("Graph unavailable", attempt.LastSendError);
        Assert.Equal(1, attempt.SendAttemptCount);
        Assert.Null(attempt.SentAt);
        Assert.Contains(events.Items, x => x.EventType == "OpenLoopChaseSendFailed");
    }

    [Fact]
    public void Policy_derives_cooldown_then_due_without_mutating_loop_status()
    {
        var loop = new GuardianOpenLoop { Status = OpenLoopStatus.Open };
        var chase = new GuardianOpenLoopChase(loop, 5, true, "");
        var sent = Prepared();
        sent.Status = GuardianChaseStatus.Sent;
        sent.SentAt = DateTimeOffset.Parse("2026-08-21T10:00:00+01:00");
        sent.NextChaseDueAt = DateTimeOffset.Parse("2026-08-26T10:00:00+01:00");

        Assert.Equal(GuardianChaseState.CoolingDown, GuardianChasePolicy.DeriveState(chase, [sent], DateTimeOffset.Parse("2026-08-25T10:00:00+01:00")));
        Assert.Equal(GuardianChaseState.Due, GuardianChasePolicy.DeriveState(chase, [sent], DateTimeOffset.Parse("2026-08-26T10:00:00+01:00")));
        Assert.Equal(OpenLoopStatus.Open, loop.Status);
    }

    private static GuardianChaseAttempt Prepared() => new()
    {
        Id = Guid.NewGuid(), OpenLoopId = Guid.NewGuid(), Status = GuardianChaseStatus.Prepared,
        AttemptNumber = 1, Recipient = "Phil Mathers", RecipientAddress = "phil@example.com",
        Subject = "Follow-up: Purchasing", Body = "Hi Phil,\n\nJust following up."
    };

    private sealed class FakeChases(params GuardianChaseAttempt[] attempts) : IGuardianChaseRepository
    {
        private readonly List<GuardianChaseAttempt> _items = [.. attempts];
        public Task<GuardianChaseAttempt?> GetPreparedAsync(Guid id) => Task.FromResult(_items.FirstOrDefault(x => x.OpenLoopId == id && x.Status == GuardianChaseStatus.Prepared));
        public Task<int> GetAttemptCountAsync(Guid id) => Task.FromResult(_items.Count(x => x.OpenLoopId == id));
        public Task AddAsync(GuardianChaseAttempt attempt) { _items.Add(attempt); return Task.CompletedTask; }
        public Task<List<GuardianChaseAttempt>> GetByStatusAsync(GuardianChaseStatus status) => Task.FromResult(_items.Where(x => x.Status == status).ToList());
        public Task<List<GuardianChaseAttempt>> GetAttemptsAsync(Guid id) => Task.FromResult(_items.Where(x => x.OpenLoopId == id).ToList());
        public Task<bool> ApproveAsync(Guid id, DateTimeOffset at) => Task.FromResult(_items.Single(x => x.Id == id).Status == GuardianChaseStatus.Prepared);
        public Task RecordSendFailureAsync(Guid id, DateTimeOffset at, string error) => Task.CompletedTask;
        public Task<bool> MarkSentAsync(Guid id, DateTimeOffset sentAt, DateTimeOffset due) => Task.FromResult(_items.Single(x => x.Id == id).Status == GuardianChaseStatus.Approved);
    }

    private sealed class FakeEmail : IEmailService
    {
        public Exception? Failure { get; init; }
        public List<string> Sent { get; } = [];
        public Task SendMessageAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default) { if (Failure is not null) throw Failure; Sent.Add(recipient); return Task.CompletedTask; }
        public Task<IReadOnlyList<EmailMessage>> GetUnreadInboxAsync(int maximum = 30, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EmailMessage>>([]);
        public Task<string> GetMessageContentAsync(string messageId, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<IReadOnlyList<string>> GetRecentSentMessageBodiesAsync(int maximum = 5, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<EmailDraftResult> CreateReplyDraftAsync(string messageId, string body, CancellationToken cancellationToken = default) => Task.FromResult(new EmailDraftResult());
        public Task UpdateDraftAsync(string draftId, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendDraftAsync(string draftId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ApplyCategoryAsync(string messageId, IReadOnlyCollection<string> existingCategories, string category, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeEvents : IWorkdayEventRepository
    {
        public List<WorkdayEvent> Items { get; } = [];
        public Task AddAsync(WorkdayEvent entry) { Items.Add(entry); return Task.CompletedTask; }
        public Task<List<WorkdayEvent>> GetBetweenAsync(DateTimeOffset from, DateTimeOffset to, int count = 500) => Task.FromResult(Items.ToList());
        public Task<List<WorkdayEvent>> SearchAsync(string query, int count = 100) => Task.FromResult(Items.ToList());
        public Task<bool> ExistsAsync(string type, string id) => Task.FromResult(Items.Any(x => x.EventType == type && x.ExternalId == id));
    }
}
