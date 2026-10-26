using Nekomata.Core.Guardian.Authority;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianDecisionOutcomeServiceTests
{
    [Fact]
    public async Task Closed_dependency_marks_accepted_project_hold_successful()
    {
        var source = Guid.NewGuid();
        var outcomes = new FakeOutcomes(Decision(source));
        var service = new GuardianDecisionOutcomeService(outcomes, new FakeProjects(new() { Id = 7, Status = "On Hold" }));

        await service.ObserveProjectOutcomesAsync([]);

        Assert.Equal(GuardianOutcomeDisposition.Successful, outcomes.Value.Outcome);
        Assert.Contains("dependency loop", outcomes.Value.OutcomeEvidence);
    }

    [Fact]
    public async Task Reactivated_project_with_open_dependency_records_mixed_outcome()
    {
        var source = Guid.NewGuid();
        var outcomes = new FakeOutcomes(Decision(source));
        var service = new GuardianDecisionOutcomeService(outcomes, new FakeProjects(new() { Id = 7, Status = "Active" }));

        await service.ObserveProjectOutcomesAsync([new GuardianOpenLoop { Id = source }]);

        Assert.Equal(GuardianOutcomeDisposition.Mixed, outcomes.Value.Outcome);
    }

    private static GuardianDecisionOutcome Decision(Guid source) => new()
    {
        ActionType = "ChangeProjectStatus", Decision = GuardianDecisionDisposition.Accepted,
        Outcome = GuardianOutcomeDisposition.Pending, ProjectId = 7, SourceEventId = source,
        DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private sealed class FakeOutcomes(GuardianDecisionOutcome value) : IGuardianDecisionOutcomeRepository
    {
        public GuardianDecisionOutcome Value { get; private set; } = value;
        public Task UpsertAsync(GuardianDecisionOutcome item) { Value = item; return Task.CompletedTask; }
        public Task<IReadOnlyList<GuardianDecisionOutcome>> GetPendingAsync(int count = 200) =>
            Task.FromResult<IReadOnlyList<GuardianDecisionOutcome>>(Value.Outcome == GuardianOutcomeDisposition.Pending ? [Value] : []);
        public Task<IReadOnlyList<GuardianDecisionOutcome>> GetRecentAsync(int count = 200) =>
            Task.FromResult<IReadOnlyList<GuardianDecisionOutcome>>([Value]);
    }

    private sealed class FakeProjects(NekomataProject project) : IProjectRepository
    {
        public Task<List<NekomataProject>> GetAllAsync() => Task.FromResult(new List<NekomataProject> { project });
        public Task<NekomataProject?> GetByIdAsync(long id) => Task.FromResult<NekomataProject?>(id == project.Id ? project : null);
        public Task<long> SaveAsync(NekomataProject value) => Task.FromResult(value.Id);
        public Task DeleteAsync(long id) => Task.CompletedTask;
    }
}
