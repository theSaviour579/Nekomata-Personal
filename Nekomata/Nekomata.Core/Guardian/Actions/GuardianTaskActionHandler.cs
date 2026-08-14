using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Data.Repositories;
using Nekomata.Models.Tasks;
using System.Text.Json;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianTaskActionHandler : IGuardianTaskActionHandler
{
    private readonly ITaskRepository _taskRepository;
    private readonly IGuardianTaskMapper _taskMapper;

    public GuardianTaskActionHandler(
        ITaskRepository taskRepository,
        IGuardianTaskMapper taskMapper)
    {
        _taskRepository = taskRepository;
        _taskMapper = taskMapper;
    }

    public async Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result)
    {
        foreach (var proposedTask in response.Tasks.Where(task => task.Selected))
        {
            var task = _taskMapper.Map(proposedTask, response.ProjectId);
            var id = await _taskRepository.SaveAsync(task);
            task.Id = id;
            result.CreatedTaskIds.Add(id);
            result.Actions.Add(new GuardianAppliedAction
            {
                Type = "Task",
                Title = proposedTask.Title,
                Description = $"Created task ({proposedTask.Priority})",
                EntityId = id,
                Operation = "Create",
                AfterState = JsonSerializer.Serialize(task),
                Reversible = true,
                Reason = response.Message,
                Confidence = response.Confidence
            });
        }

        foreach (var change in response.Changes.Where(change =>
                     change.Selected &&
                     change.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase)))
        {
            await ApplyChangeAsync(change, result);
        }

        result.TasksCreated = result.CreatedTaskIds.Count;
    }

    private async Task ApplyChangeAsync(
        GuardianChange change,
        GuardianApplyResult result)
    {
        if (change.EntityId <= 0)
        {
            result.Messages.Add(
                $"Task change '{change.Property}' was not applied because it has no valid task ID.");
            return;
        }

        var task = await _taskRepository.GetByIdAsync(change.EntityId);
        if (task is null)
        {
            result.Messages.Add($"Task {change.EntityId} was not found; no change was applied.");
            return;
        }

        if (!GuardianTaskChangeNormalizer.TryNormalizeStatus(
                change,
                out var status,
                out var note))
        {
            result.Messages.Add(
                $"Task {change.EntityId} has an unsupported {change.Property} value '{change.NewValue}'.");
            return;
        }

        var beforeState = JsonSerializer.Serialize(task);
        if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            await _taskRepository.CompleteAsync(task.Id);
        }

        else if (status.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            await _taskRepository.ReopenAsync(task.Id);
        }
        else
        {
            task.Status = status;
            task.CompletedAt = null;
            AppendClosureNote(task, note);
            await _taskRepository.SaveAsync(task);
        }

        var updatedTask = await _taskRepository.GetByIdAsync(task.Id);

        result.Actions.Add(new GuardianAppliedAction
        {
            Type = "Task",
            Title = $"{task.Title} → {status}",
            Description = note is null
                ? $"Status changed to {status}."
                : $"Status changed to {status}. Note: {note}",
            EntityId = task.Id,
            Operation = "Update",
            BeforeState = beforeState,
            AfterState = updatedTask is null ? null : JsonSerializer.Serialize(updatedTask),
            Reversible = updatedTask is not null,
            IrreversibleReason = updatedTask is null ? "The updated task could not be read back." : null,
            Reason = change.Reason,
            Confidence = change.Confidence
        });
    }

    private static void AppendClosureNote(NekomataTask task, string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return;

        var closureNote = $"Guardian closure note: {note}";
        if (task.Description?.Contains(closureNote, StringComparison.OrdinalIgnoreCase) == true)
            return;

        task.Description = string.IsNullOrWhiteSpace(task.Description)
            ? closureNote
            : $"{task.Description}{Environment.NewLine}{Environment.NewLine}{closureNote}";
    }
}
