using System.Text.Json;
using System.Text.Json.Nodes;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;

namespace Nekomata.UI.Services;

public sealed class GuardianUndoService
{
    private readonly IGuardianAuditRepository _audit;
    private readonly ITaskRepository _tasks;
    private readonly IProjectRepository _projects;
    private readonly ICalendarService _calendar;

    public GuardianUndoService(IGuardianAuditRepository audit, ITaskRepository tasks,
        IProjectRepository projects, ICalendarService calendar)
    {
        _audit = audit;
        _tasks = tasks;
        _projects = projects;
        _calendar = calendar;
    }

    public async Task<GuardianUndoResult> UndoBatchAsync(Guid batchId)
    {
        var entries = await _audit.GetBatchAsync(batchId);
        if (entries.Count == 0) return new(false, "That Guardian action batch no longer exists.");
        if (entries.Any(entry => !entry.CanUndo))
            return new(false, "This batch is already undone or contains an irreversible action.");

        foreach (var entry in entries)
        {
            var conflict = await FindConflictAsync(entry);
            if (conflict is null) continue;
            await _audit.MarkBatchConflictAsync(batchId, conflict);
            return new(false, conflict);
        }

        foreach (var entry in entries.OrderByDescending(entry => entry.Id))
            await UndoEntryAsync(entry);

        var message = $"Safely undid {entries.Count} Guardian action{(entries.Count == 1 ? "" : "s")}.";
        await _audit.MarkBatchUndoneAsync(batchId, message);
        return new(true, message);
    }

    private async Task<string?> FindConflictAsync(GuardianAuditEntry entry)
    {
        if (entry.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase) && entry.EntityId is long taskId)
        {
            var current = await _tasks.GetByIdAsync(taskId);
            if (entry.Operation == "Create")
                return current is not null && Same(current, entry.AfterState) ? null : $"Undo stopped: task {taskId} has changed or was removed.";
            return current is not null && Same(current, entry.AfterState) ? null : $"Undo stopped: task {taskId} was edited after Guardian changed it.";
        }

        if (entry.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase) && entry.EntityId is long projectId)
        {
            var current = await _projects.GetByIdAsync(projectId);
            return current is not null && Same(current, entry.AfterState) ? null : $"Undo stopped: project {projectId} was edited after Guardian changed it.";
        }

        if (entry.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
        {
            var expected = Deserialize<CalendarAuditSnapshot>(entry.AfterState);
            if (expected is null) return "Undo stopped: the saved calendar state is incomplete.";
            var current = await FindCalendarEventAsync(expected);
            return current is not null && CalendarMatches(current, expected)
                ? null
                : $"Undo stopped: calendar event '{expected.Subject}' has changed or was removed.";
        }

        return $"Undo is not supported for {entry.EntityType} actions.";
    }

    private async Task UndoEntryAsync(GuardianAuditEntry entry)
    {
        if (entry.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase) && entry.EntityId is long taskId)
        {
            if (entry.Operation == "Create") await _tasks.DeleteAsync(taskId);
            else
            {
                var before = Deserialize<NekomataTask>(entry.BeforeState) ?? throw new InvalidOperationException("Missing task snapshot.");
                await _tasks.SaveAsync(before);
            }
            return;
        }

        if (entry.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            var before = Deserialize<NekomataProject>(entry.BeforeState) ?? throw new InvalidOperationException("Missing project snapshot.");
            await _projects.SaveAsync(before);
            return;
        }

        var calendarBefore = Deserialize<CalendarAuditSnapshot>(entry.BeforeState);
        if (entry.Operation == "Create") await _calendar.DeleteFocusEventAsync(entry.ExternalId!);
        else if (calendarBefore is not null)
            await _calendar.MoveFocusEventAsync(entry.ExternalId!, calendarBefore.Start, calendarBefore.End);
    }

    private async Task<CalendarEvent?> FindCalendarEventAsync(CalendarAuditSnapshot expected)
    {
        var start = expected.Start.AddDays(-1);
        var end = expected.End.AddDays(1);
        return (await _calendar.GetEventsAsync(start, end)).FirstOrDefault(item => item.Id == expected.Id);
    }

    private static bool CalendarMatches(CalendarEvent current, CalendarAuditSnapshot expected) =>
        current.Subject == expected.Subject && current.Start == expected.Start && current.End == expected.End;

    private static bool Same<T>(T current, string? expected)
    {
        if (expected is null) return false;
        return JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.Serialize(current)), JsonNode.Parse(expected));
    }

    private static T? Deserialize<T>(string? value) =>
        string.IsNullOrWhiteSpace(value) ? default : JsonSerializer.Deserialize<T>(value);

    private sealed record CalendarAuditSnapshot(string Id, string Subject, DateTimeOffset Start, DateTimeOffset End);
}

public sealed record GuardianUndoResult(bool Success, string Message);
