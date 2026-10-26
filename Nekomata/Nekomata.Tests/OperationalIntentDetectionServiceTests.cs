using Nekomata.Core.Guardian.Anticipation;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class OperationalIntentDetectionServiceTests
{
    [Fact]
    public async Task Extracts_scheduling_intent_from_correlated_teams_request()
    {
        var thread = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-21T08:00:00+01:00");
        var signal = new ContextSignal
        {
            Id = Guid.NewGuid(), Source = "Microsoft Teams", Kind = ContextSignalKind.Conversation,
            Person = "Craig Cranfield", Title = "Alex, have you got half an hour tomorrow morning to go through this change control?",
            Body = "Alex, have you got half an hour tomorrow morning to go through this change control?",
            ObservedAt = now.AddMinutes(-5), WorkThreadId = thread
        };

        var result = await new OperationalIntentDetectionService(new FakeSignals(signal)).DetectAsync(now);

        var intent = Assert.Single(result);
        Assert.Equal(OperationalIntentType.ScheduleMeeting, intent.Type);
        Assert.Equal("Craig Cranfield", intent.Person);
        Assert.Equal(30, intent.DurationMinutes);
        Assert.Equal("Tomorrow morning", intent.RequestedWindow);
        Assert.Equal(thread, intent.WorkThreadId);
        Assert.Equal(100, intent.Confidence);
    }

    [Fact]
    public async Task Ignores_davids_own_messages()
    {
        var now = DateTimeOffset.Now;
        var result = await new OperationalIntentDetectionService(new FakeSignals(new ContextSignal
        {
            Source = "Microsoft Teams", Kind = ContextSignalKind.Conversation, Person = "Alex Morgan",
            Body = "I can arrange 30 minutes tomorrow morning", Title = "I can arrange 30 minutes tomorrow morning", ObservedAt = now
        })).DetectAsync(now);
        Assert.Empty(result);
    }

    private sealed class FakeSignals(params ContextSignal[] values) : IContextSignalRepository
    {
        public Task<ContextSignal> UpsertAsync(ContextSignal value) => Task.FromResult(value);
        public Task<List<ContextSignal>> GetCurrentAsync(int count = 1000) => Task.FromResult(values.ToList());
        public Task ResolveAsync(Guid id, ContextSignalDisposition disposition, Guid? threadId, long? taskId, string resolution) => Task.CompletedTask;
    }
}
