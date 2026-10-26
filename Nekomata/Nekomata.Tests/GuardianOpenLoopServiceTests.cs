using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianOpenLoopServiceTests
{
    [Fact]
    public async Task Resolves_matching_external_signal_with_expected_evidence()
    {
        var repository = new FakeRepository(new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Title = "Waiting for reply", Source = "GraphMail", EmailId = "thread-42",
            SourceEventId = Guid.NewGuid(), ExpectedEvidenceKind = "ReplyReceived"
        });
        var service = new GuardianOpenLoopService(repository);

        var result = await service.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), Kind = "ReplyReceived", EmailId = "thread-42", Summary = "Alex replied"
        });

        Assert.Single(result);
        Assert.Equal(OpenLoopStatus.Resolved, repository.Items[0].Status);
        Assert.NotNull(repository.Items[0].ResolutionEventId);
    }

    [Fact]
    public async Task Does_not_resolve_on_context_match_when_evidence_kind_is_wrong()
    {
        var repository = new FakeRepository(new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Title = "Decision required", Source = "Teams", ProjectId = 7,
            SourceEventId = Guid.NewGuid(), ExpectedEvidenceKind = "DecisionRecorded"
        });
        var service = new GuardianOpenLoopService(repository);

        var result = await service.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), Kind = "MessageReceived", ProjectId = 7, Summary = "New discussion"
        });

        Assert.Empty(result);
        Assert.Equal(OpenLoopStatus.Open, repository.Items[0].Status);
    }

    [Fact]
    public async Task Explicit_loop_reference_resolves_without_inferred_context()
    {
        var loop = new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Title = "Ask finance", Source = "Manual", SourceEventId = Guid.NewGuid()
        };
        var repository = new FakeRepository(loop);
        var service = new GuardianOpenLoopService(repository);

        var result = await service.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), OpenLoopId = loop.Id, Kind = "UserConfirmed", Summary = "Asked"
        });

        Assert.Single(result);
    }

    [Fact]
    public async Task Project_only_match_is_returned_for_review_not_auto_resolved()
    {
        var repository = new FakeRepository(new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Title = "Waiting for project decision", Source = "Manual", ProjectId = 7,
            SourceEventId = Guid.NewGuid(), ExpectedEvidenceKind = "DecisionRecorded"
        });
        var service = new GuardianOpenLoopService(repository);

        var review = await service.EvaluateEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), Kind = "DecisionRecorded", ProjectId = 7, Summary = "A decision was mentioned"
        });
        var resolved = await service.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), Kind = "DecisionRecorded", ProjectId = 7, Summary = "A decision was mentioned"
        });

        Assert.Equal(OpenLoopEvidenceDisposition.NeedsReview, Assert.Single(review).Disposition);
        Assert.Empty(resolved);
    }

    [Fact]
    public async Task What_am_i_waiting_on_returns_only_awaiting_them()
    {
        var repository = new FakeRepository(
            new GuardianOpenLoop { Id = Guid.NewGuid(), Type = OpenLoopType.AwaitingThem, Title = "Their reply", Source = "Mail", SourceEventId = Guid.NewGuid() },
            new GuardianOpenLoop { Id = Guid.NewGuid(), Type = OpenLoopType.AwaitingMe, Title = "My prep", Source = "Calendar", SourceEventId = Guid.NewGuid() });
        var service = new GuardianOpenLoopService(repository);

        var result = await service.GetWhatAmIWaitingOnAsync();

        Assert.Equal("Their reply", Assert.Single(result).Title);
    }

    [Fact]
    public async Task Capture_normalises_graph_timestamp_offset_to_utc()
    {
        var repository = new FakeRepository();
        var service = new GuardianOpenLoopService(repository);
        var sourceTime = new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.FromHours(-1));

        var captured = await service.CaptureAsync(new GuardianOpenLoop
        {
            Title = "Waiting for reply", Source = "Email", SourceEventId = Guid.NewGuid(),
            CreatedAt = sourceTime, DueAt = sourceTime.AddDays(2)
        });

        Assert.Equal(TimeSpan.Zero, captured.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, captured.UpdatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, captured.DueAt?.Offset);
        Assert.Equal(sourceTime.UtcDateTime, captured.CreatedAt.UtcDateTime);
    }

    [Fact]
    public async Task Resolution_normalises_evidence_timestamp_offset_to_utc()
    {
        var loop = new GuardianOpenLoop
        {
            Id = Guid.NewGuid(), Title = "Waiting", Source = "Email", SourceEventId = Guid.NewGuid()
        };
        var repository = new FakeRepository(loop);
        var service = new GuardianOpenLoopService(repository);
        var evidence = new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), OpenLoopId = loop.Id,
            OccurredAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(1))
        };

        await service.ResolveFromEvidenceAsync(evidence);

        Assert.Equal(TimeSpan.Zero, evidence.OccurredAt.Offset);
        Assert.Equal(TimeSpan.Zero, loop.ResolvedAt?.Offset);
    }

    private sealed class FakeRepository(params GuardianOpenLoop[] items) : IGuardianOpenLoopRepository
    {
        public List<GuardianOpenLoop> Items { get; } = [.. items];
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop openLoop) { Items.Add(openLoop); return Task.FromResult(openLoop); }
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) =>
            Task.FromResult(Items.Where(x => x.Status == OpenLoopStatus.Open).Take(count).ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) =>
            Task.FromResult(Items.Where(x => x.Status == OpenLoopStatus.Open && x.Type == OpenLoopType.AwaitingThem).Take(count).ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence)
        {
            var item = Items.Single(x => x.Id == id);
            if (item.Status != OpenLoopStatus.Open) return Task.FromResult(false);
            item.Status = OpenLoopStatus.Resolved;
            item.ResolvedAt = evidence.OccurredAt;
            item.ResolutionEventId = evidence.EventId;
            item.ResolutionSummary = evidence.Summary;
            return Task.FromResult(true);
        }
    }
}
