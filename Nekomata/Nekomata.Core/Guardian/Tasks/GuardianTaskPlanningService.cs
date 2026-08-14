using System.Text;
using Nekomata.AI.Interfaces;
using Nekomata.Models.AI;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Tasks;

public class GuardianTaskPlanningService
    : IGuardianTaskPlanningService
{
    private readonly IAIProvider
        _aiProvider;

    public GuardianTaskPlanningService(
        IAIProvider aiProvider)
    {
        _aiProvider =
            aiProvider;
    }

    public async Task<GuardianTaskActionPlan> BuildPlanAsync(
        string instruction,
        NekomataWorkspace workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction);

        ArgumentNullException.ThrowIfNull(
            workspace);

        var prompt =
            BuildPrompt(
                instruction,
                workspace);

        var plan =
            await _aiProvider
                .AskJsonAsync<GuardianTaskActionPlan>(
                    prompt);

        return plan
               ?? new GuardianTaskActionPlan
               {
                   Summary =
                       "Guardian could not create a valid task plan.",

                   Questions =
                   [
                       "Please rephrase the request with the task title, " +
                       "priority, deadline and project where applicable."
                   ]
               };
    }

    private static string BuildPrompt(
        string instruction,
        NekomataWorkspace workspace)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "You are Guardian, a task-planning assistant.");

        builder.AppendLine();
        builder.AppendLine(
            "Convert the user's instruction into a structured task action plan.");

        builder.AppendLine();
        builder.AppendLine(
            "Allowed ActionType values:");

        builder.AppendLine(
            "Add, Update, Complete, Reopen, Delete");

        builder.AppendLine();
        builder.AppendLine(
            "Rules:");

        builder.AppendLine(
            "- Every action must include a Confidence value between 0 and 100.");

        builder.AppendLine(
            "- Every action must include one or more ConfidenceReasons explaining why the confidence was chosen.");

        builder.AppendLine(
            "- If confidence is below 80, explain what information is missing.");

        builder.AppendLine(
            "- Never use 100 unless the task or project match is exact and unambiguous.");

        builder.AppendLine(
            "- Do not invent task IDs or project IDs.");

        builder.AppendLine(
            "- Use an existing task ID only when the request clearly matches one task.");

        builder.AppendLine(
            "- Use an existing project ID only when the project name clearly matches.");

        builder.AppendLine(
            "- Ask a question when multiple tasks or projects could match.");

        builder.AppendLine(
            "- Use Priority values Critical, High, Normal or Low.");

        builder.AppendLine(
            "- Use ISO 8601 date/time values.");

        builder.AppendLine(
            "- Set RequiresConfirmation to true for every action.");

        builder.AppendLine(
            "- Never execute work. Only return the proposed action plan.");

        builder.AppendLine();
        builder.AppendLine(
            $"Current local time: {DateTime.Now:O}");

        builder.AppendLine();

        builder.AppendLine(
            "Existing projects:");

        foreach (var project in workspace.Projects)
        {
            builder.AppendLine(
                $"- ID={project.Id}; " +
                $"Name={project.Name}; " +
                $"Status={project.Status}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "Existing open tasks:");

        foreach (var task in workspace.Tasks)
        {
            builder.AppendLine(
                $"- ID={task.Id}; " +
                $"Title={task.Title}; " +
                $"ProjectId={task.ProjectId?.ToString() ?? "none"}; " +
                $"Status={task.Status}; " +
                $"Priority={task.Priority}; " +
                $"DueAt={task.DueAt?.ToString("O") ?? "none"}");
        }

        builder.AppendLine();
        builder.AppendLine(
            "User instruction:");

        builder.AppendLine(
            instruction);

        builder.AppendLine();
        builder.AppendLine(
            "Return JSON matching GuardianTaskActionPlan exactly.");

        return builder.ToString();
    }
}