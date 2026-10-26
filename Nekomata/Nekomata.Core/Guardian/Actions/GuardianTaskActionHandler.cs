using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Mapping;
using Nekomata.Data.Repositories;
using Nekomata.Models.Tasks;
using Nekomata.Core.Guardian.OpenLoops;
using System.Text.Json;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianTaskActionHandler : IGuardianTaskActionHandler
{
    private readonly ITaskRepository _taskRepository;
    private readonly IGuardianTaskMapper _taskMapper;
    private readonly GuardianOpenLoopIngestionService? _openLoops;

    public GuardianTaskActionHandler(
        ITaskRepository taskRepository,
        IGuardianTaskMapper taskMapper,
        GuardianOpenLoopIngestionService? openLoops = null)
    {
        _taskRepository = taskRepository;
        _taskMapper = taskMapper;
        _openLoops = openLoops;
    }

    public async Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result)
    {
        foreach (var proposedTask in response.Tasks.Where(task => task.Selected))
        {
            var task = _taskMapper.Map(proposedTask, proposedTask.ProjectId ?? response.ProjectId);
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

        if (change.Property.Equals("DueAt", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateTime.TryParse(change.NewValue, out var dueAt))
            {
                result.Messages.Add($"Task {change.EntityId} has an invalid recovery date '{change.NewValue}'.");
                return;
            }
            var before = JsonSerializer.Serialize(task);
            task.DueAt = dueAt;
            await _taskRepository.SaveAsync(task);
            var updated = await _taskRepository.GetByIdAsync(task.Id);
            result.Actions.Add(new GuardianAppliedAction
            {
                Type = "Task", Title = $"Moved {task.Title}", Description = $"Deferred to {dueAt:ddd dd MMM}.",
                EntityId = task.Id, Operation = "Update", BeforeState = before,
                AfterState = updated is null ? null : JsonSerializer.Serialize(updated), Reversible = updated is not null,
                IrreversibleReason = updated is null ? "The deferred task could not be read back." : null,
                Reason = change.Reason, Confidence = change.Confidence
            });
            return;
        }

        var beforeState = JsonSerializer.Serialize(task);
        if (change.Property.Equals("Priority", StringComparison.OrdinalIgnoreCase))
        {
            var allowed = new[] { "Low", "Normal", "High", "Critical" };
            var priority = allowed.FirstOrDefault(value =>
                value.Equals(change.NewValue, StringComparison.OrdinalIgnoreCase));
            if (priority is null)
            {
                result.Messages.Add($"Task {change.EntityId} has an invalid priority '{change.NewValue}'.");
                return;
            }
            task.Priority = priority;
            await _taskRepository.SaveAsync(task);
            await RecordUpdatedTaskAsync(task, beforeState, change, result, $"Priority changed to {priority}.");
            return;
        }

        if (change.Property.Equals("Owner", StringComparison.OrdinalIgnoreCase))
        {
            var owner = change.NewValue.Trim();
            if (owner.Length is 0 or > 120)
            {
                result.Messages.Add($"Task {change.EntityId} has an invalid owner '{change.NewValue}'.");
                return;
            }
            task.Owner = owner;
            await _taskRepository.SaveAsync(task);
            await RecordUpdatedTaskAsync(task, beforeState, change, result, $"Handed over to {owner}.");
            return;
        }

        if (change.Property.Equals("EstimatedMinutes", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(change.NewValue, out var minutes) || minutes <= 0)
            {
                result.Messages.Add($"Task {change.EntityId} has an invalid estimate '{change.NewValue}'.");
                return;
            }
            task.EstimatedMinutes = minutes;
            await _taskRepository.SaveAsync(task);
            await RecordUpdatedTaskAsync(task, beforeState, change, result, $"Estimate changed to {minutes} minutes.");
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

        if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
        {
            await _taskRepository.CompleteAsync(task.Id);
            if (_openLoops is not null)
                await _openLoops.IngestTaskCompletionAsync(task.Id, task.Title);
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

    private async Task RecordUpdatedTaskAsync(
        NekomataTask task,
        string beforeState,
        GuardianChange change,
        GuardianApplyResult result,
        string description)
    {
        var updatedTask = await _taskRepository.GetByIdAsync(task.Id);
        result.Actions.Add(new GuardianAppliedAction
        {
            Type = "Task", Title = task.Title, Description = description,
            EntityId = task.Id, Operation = "Update", BeforeState = beforeState,
            AfterState = updatedTask is null ? null : JsonSerializer.Serialize(updatedTask),
            Reversible = updatedTask is not null,
            IrreversibleReason = updatedTask is null ? "The updated task could not be read back." : null,
            Reason = change.Reason, Confidence = change.Confidence
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
