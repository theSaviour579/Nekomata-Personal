using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Actions;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Data.Repositories;
using Nekomata.Models.Tasks;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianTaskActionHandlerTests
{
    [Fact]
    public async Task Malformed_cancelled_status_is_normalized_and_note_is_preserved()
    {
        var repository = new FakeTaskRepository(new NekomataTask
        {
            Id = 3,
            Title = "Roam Zone Analysis",
            Description = "Original task",
            Status = "Open"
        });
        var handler = new GuardianTaskActionHandler(repository, new StubTaskMapper());
        var response = Response(new GuardianChange
        {
            EntityType = "Task",
            EntityId = 3,
            Property = "Status",
            NewValue = "Cancelled – no longer required pending roam zone overhaul"
        });
        var result = new GuardianApplyResult { Success = true };

        await handler.ApplyAsync(response, result);

        Assert.Equal("Cancelled", repository.Task!.Status);
        Assert.Contains("Guardian closure note: no longer required pending roam zone overhaul", repository.Task.Description);
        var action = Assert.Single(result.Actions);
        Assert.Equal(3, action.EntityId);
        Assert.Contains("Cancelled", action.Title);
    }

    [Fact]
    public async Task Completed_status_uses_repository_completion_semantics()
    {
        var repository = new FakeTaskRepository(new NekomataTask
        {
            Id = 7,
            Title = "Finish report",
            Status = "Open"
        });
        var handler = new GuardianTaskActionHandler(repository, new StubTaskMapper());
        var result = new GuardianApplyResult { Success = true };

        await handler.ApplyAsync(Response(new GuardianChange
        {
            EntityType = "Task",
            EntityId = 7,
            Property = "Status",
            NewValue = "Completed"
        }), result);

        Assert.True(repository.CompleteCalled);
        Assert.Equal("Completed", repository.Task!.Status);
        Assert.Single(result.Actions);
    }

    [Fact]
    public async Task Missing_task_is_reported_and_not_counted_as_applied()
    {
        var repository = new FakeTaskRepository(null);
        var handler = new GuardianTaskActionHandler(repository, new StubTaskMapper());
        var result = new GuardianApplyResult { Success = true };

        await handler.ApplyAsync(Response(new GuardianChange
        {
            EntityType = "Task",
            EntityId = 999,
            Property = "Status",
            NewValue = "Cancelled"
        }), result);

        Assert.Empty(result.Actions);
        Assert.Contains(result.Messages, message => message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unsupported_status_is_rejected()
    {
        var change = new GuardianChange
        {
            EntityType = "Task",
            EntityId = 3,
            Property = "Status",
            NewValue = "Cancelled someday maybe"
        };

        var valid = GuardianTaskChangeNormalizer.TryNormalizeStatus(
            change, out var status, out var note);

        Assert.True(valid);
        Assert.Equal("Cancelled", status);
        Assert.Equal("someday maybe", note);
    }

    private static GuardianActionResponse Response(GuardianChange change) => new()
    {
        Changes = [change]
    };

    private sealed class StubTaskMapper : IGuardianTaskMapper
    {
        public NekomataTask Map(ProposedTask proposedTask, long? projectId) => new();
    }

    private sealed class FakeTaskRepository : ITaskRepository
    {
        public FakeTaskRepository(NekomataTask? task) => Task = task;

        public NekomataTask? Task { get; private set; }
        public bool CompleteCalled { get; private set; }

        public Task<List<NekomataTask>> GetOpenTasksAsync() =>
            System.Threading.Tasks.Task.FromResult(
                Task is null ? [] : new List<NekomataTask> { Task });

        public Task<NekomataTask?> GetByIdAsync(long id) =>
            System.Threading.Tasks.Task.FromResult(Task?.Id == id ? Task : null);

        public Task<long> SaveAsync(NekomataTask task)
        {
            Task = task;
            return System.Threading.Tasks.Task.FromResult(task.Id);
        }

        public Task DeleteAsync(long id) => System.Threading.Tasks.Task.CompletedTask;

        public Task CompleteAsync(long taskId)
        {
            CompleteCalled = true;
            if (Task?.Id == taskId)
            {
                Task.Status = "Completed";
                Task.CompletedAt = DateTime.Now;
            }
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task ReopenAsync(long taskId)
        {
            if (Task?.Id == taskId)
            {
                Task.Status = "Open";
                Task.CompletedAt = null;
            }
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public Task<int> CompleteOpenTasksForProjectAsync(long projectId) =>
            System.Threading.Tasks.Task.FromResult(0);
    }
}