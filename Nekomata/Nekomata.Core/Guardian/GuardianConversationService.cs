using Nekomata.AI.Interfaces;
using Nekomata.AI.Models;
using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian;

public class GuardianConversationService
    : IGuardianConversationService
{
    private readonly IStructuredAIProvider _structuredAIProvider;

    private readonly IGuardianMemoryRepository
        _memoryRepository;

    public GuardianConversationService(
        IStructuredAIProvider structuredAIProvider,
        IGuardianMemoryRepository memoryRepository)
    {
        _structuredAIProvider =
            structuredAIProvider;

        _memoryRepository =
            memoryRepository;
    }

    public async Task<GuardianActionResponse> AskAsync(
     GuardianConversationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var projectsContext =
            BuildProjectsContext(request.Workspace);

        var tasksContext =
            BuildTasksContext(request.Workspace);

        var integrationContext =
            BuildIntegrationContext(request.Workspace);

        var conversationContext =
    BuildConversationContext(
        request.Conversation);

        var recentMemories =
    await _memoryRepository.GetRecentAsync(10);

        var memoryContext =
            BuildMemoryContext(recentMemories);

        var prompt =
            BuildPrompt(
                request.UserMessage,
                memoryContext,
                projectsContext,
                tasksContext,
                integrationContext,
                conversationContext,
                request.Workspace,
                request.CurrentTime);
        const string systemPrompt =
            """
You are Guardian.

You are the executive AI inside Nekomata.

Always return JSON that matches the supplied schema exactly.

Do not return markdown.

Do not return code fences.

The "message" field should contain the conversational response shown to the user.

Populate "changes" whenever you recommend modifying projects, tasks, missions, workspace data or the calendar.

For an existing task change, use EntityType "Task", EntityId as the exact Task ID from OPEN TASKS, and Property "Status". NewValue must be exactly "Completed", "Cancelled", or "Open"; put explanatory context in Reason, never in NewValue. Never claim a task cannot be changed when its Task ID is supplied.

Calendar operations are explicit proposals. Use EntityType "Calendar".
To create a block, use Property "CreateFocusBlock", EntityId as the related task ID or 0, OldValue empty, and NewValue exactly "ISO_START|ISO_END|TITLE".
To move an existing block, use Property "MoveNekomataBlock", EntityId 0, OldValue as the exact calendar event ID, and NewValue exactly "ISO_START|ISO_END".
Only events marked MOVABLE=true may be moved. Never move, replace, shorten or overlap a protected meeting.
The calendar snapshot is authoritative. Never merge events, extend their times, omit events, or describe a period as free when an event overlaps it. Quote every event time exactly as supplied. For gap-filling proposals, the structured start and end must match the stated free-window boundaries; never extend a block to its estimated task duration beyond that gap.
When the user asks to plan one or more covered dates, calculate gaps separately for each DATE section and propose concrete Calendar changes immediately. Calendar events override the default lunch window. Never invent a free period or say a covered date cannot be seen. If a covered date has no events, treat the working day as open. Use a reasonable close-of-work assumption when a demo-ready time is unspecified, state that assumption, and do not ask an avoidable follow-up question. The conversational message and every structured change must use identical times.
Use exact local ISO timestamps with an offset. If the user has not supplied enough scheduling detail, ask a question instead of guessing.

Populate "tasks" whenever new tasks should be created.

Populate "questions" only if clarification is genuinely required.

If no changes are required, return an empty changes array.

If no tasks are required, return an empty tasks array.
""";

        var response =
    await _structuredAIProvider
        .AskStructuredAsync<GuardianActionResponse>(
            systemPrompt,
            prompt);

return response ??
       new GuardianActionResponse
       {
           Message = "Guardian couldn't produce a structured response."
       };
    }

    private static string BuildPrompt(
    string userMessage,
    string memoryContext,
    string projectsContext,
    string tasksContext,
    string integrationContext,
    string conversationContext,
    NekomataWorkspace workspace,
    DateTime currentTime)
    {
        var mission =
            workspace.CurrentMission;

        return $"""
        You are Guardian, Nekomata's executive AI.

        You assist an experienced IT Manager.

        Be conversational, concise and commercially aware.

        Do not use Markdown.

        Avoid headings unless they improve readability.

        Prefer short paragraphs and bullet points.

        Use the user's workspace to provide recommendations rather than generic advice.

        When information is missing, ask only the minimum questions needed to continue.

        Avoid repeating information already visible in the workspace.

        Think like an experienced Head of IT rather than a chatbot.

        CURRENT LOCAL TIME
        Date and time: {currentTime:dddd dd MMMM yyyy HH:mm:ss}
        Time zone: {TimeZoneInfo.Local.DisplayName}

        USER WORKING HOURS
        Monday to Friday: 08:00 to 16:30
        Lunch: 12:30 to 13:30

        CURRENT MISSION
        Title: {mission?.Title ?? "No mission selected"}
        Score: {mission?.Score ?? 0}
        Business value: {mission?.BusinessValue ?? 0:C}
        Estimated duration: {mission?.EstimatedDuration ?? TimeSpan.Zero}
        Threat level: {mission?.ThreatLevel ?? "None"}

        OPEN TASKS
        {tasksContext}

        HALO AND OTHER INTEGRATION TICKETS
        {integrationContext}

        PROJECTS
        {projectsContext}

        RECENT MEMORY
        {memoryContext}

        RECENT CONVERSATION
        {conversationContext}

        LATEST USER MESSAGE
        {userMessage}

        Use the mission, project, task, memory and conversation
        context above when answering.

        Respect the user's working hours and lunch break.

        Be practical, concise and commercially aware.

        Treat current workspace data as more recent than
        historical memory.

        The current mission and live calendar block are execution truth: call them the current objective.
        A project's Business value is its total strategic exposure, not value delivered by one task or focus block.
        Distinguish the highest strategic-value project from the current executable objective.
        Portfolio totals are aggregate values and must never be attributed to an individual project.
        Do not say one item dwarfs other priorities unless you name the compared records and their individual values.

        Do not claim to have changed a database record unless
        Nekomata explicitly confirms the action.
        """;
    }

    private static string BuildProjectContext(
    NekomataProject project)
    {
        var lifecycle =
            string.Equals(
                project.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase)
                ? "Completed / archived"
                : "Active";

        return $"""
        Project ID: {project.Id}
        Name: {project.Name}
        Description: {project.Description ?? "No description"}
        Lifecycle: {lifecycle}
        Status: {project.Status}
        Priority: {project.Priority}
        Progress: {project.ProgressPercent}%
        Business value: {project.EstimatedBusinessValue:C}
        Remaining effort: {project.EstimatedRemainingMinutes} minutes
        Due: {project.DueAt?.ToString("dd MMM yyyy") ?? "No due date"}
        At risk: {project.AtRisk}
        Next action: {project.NextAction ?? "No next action"}
        """;
    }

    private static string BuildProjectsContext(
    NekomataWorkspace workspace)
    {
        if (workspace.Projects.Count == 0)
            return "No projects currently loaded.";

        var activeProjects = workspace.Projects.Where(project =>
            !string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "Closed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(project.Status, "On Hold", StringComparison.OrdinalIgnoreCase)).ToList();
        var aggregate = activeProjects.Sum(project => project.EstimatedBusinessValue);
        var header = $"Aggregate active portfolio value: {aggregate:C}. This is a portfolio total, not an individual project value.";

        return header + Environment.NewLine + Environment.NewLine + string.Join(
            Environment.NewLine +
            Environment.NewLine,
            workspace.Projects.Select(BuildProjectContext));
    }

    private static string BuildTaskContext(
    NekomataTask task)
    {
        return $"""
        Task ID: {task.Id}
        Project ID: {task.ProjectId?.ToString() ?? "None"}
        Title: {task.Title}
        Priority score: {task.PriorityScore}
        Priority: {task.Priority}
        Estimated time: {task.EstimatedMinutes} minutes
        Business value: {task.EstimatedBusinessValue:C}
        Due: {task.DueAt?.ToString("dd MMM yyyy HH:mm") ?? "No due date"}
        """;
    }

    private static string BuildTasksContext(
        NekomataWorkspace workspace)
    {
        if (workspace.Tasks.Count == 0)
            return "No open tasks currently loaded.";

        return string.Join(
            Environment.NewLine +
            Environment.NewLine,
            workspace.Tasks.Select(BuildTaskContext));
    }

    private static string BuildIntegrationContext(
        NekomataWorkspace workspace)
    {
        if (workspace.IntegrationMissionCandidates.Count == 0)
            return "No integration tickets currently loaded.";

        return string.Join(
            Environment.NewLine +
            Environment.NewLine,
            workspace.IntegrationMissionCandidates.Select(candidate => $"""
            Source: {candidate.SourceType}
            Ticket ID: {candidate.SourceRecordId ?? "Unknown"}
            Title: {candidate.Title}
            Status: {candidate.Description}
            Priority: {candidate.Priority}
            Score: {candidate.Score}
            Estimated time: {candidate.EstimatedMinutes} minutes
            Business value: {candidate.BusinessValue:C}
            Due: {candidate.DueAt?.ToString("dd MMM yyyy HH:mm") ?? "No due date"}
            Immediate attention: {candidate.RequiresImmediateAttention}
            Last updated: {candidate.LastUpdatedAt?.ToString("dd MMM yyyy HH:mm") ?? "Unknown"}
            Awaiting external response: {candidate.IsAwaitingExternalResponse}
            Available for active planning: {candidate.IsActionable}
            """));
    }

    private static string BuildConversationContext(
    IReadOnlyList<GuardianChatTurn> conversation)
    {
        return string.Join(
            Environment.NewLine +
            Environment.NewLine,

            conversation
                .TakeLast(12)
                .Select(message => $"""
                {message.Role.ToUpperInvariant()}:
                {message.Content}
                """));
    }

    private static string BuildMemoryContext(
    IReadOnlyList<GuardianMemory> memories)
    {
        if (memories.Count == 0)
            return "No previous memory.";

        return string.Join(
            Environment.NewLine +
            Environment.NewLine,

            memories.Select(memory => $"""
            Category: {memory.Category}
            Importance: {memory.Importance}
            Summary: {memory.Summary}
            Detail: {memory.Detail ?? "No additional detail"}
            Recorded: {memory.CreatedAt:dd MMM yyyy HH:mm}
            """));
    }
}