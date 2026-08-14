using Nekomata.AI.Interfaces;
using Nekomata.AI.Models.Meetings;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Meetings;

public class MeetingAnalysisService
    : IMeetingAnalysisService
{
    private readonly IStructuredAIProvider
    _structuredAIProvider;

    public MeetingAnalysisService(
        IStructuredAIProvider structuredAIProvider)
    {
        _structuredAIProvider =
            structuredAIProvider;
    }

    public async Task<MeetingAnalysisResponse> AnalyseAsync(
        string meetingNotes,
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var prompt =
            BuildPrompt(
                meetingNotes,
                workspace);

        var result =
      await _structuredAIProvider
          .AskStructuredAsync<MeetingAnalysisResponse>(
              """
            You are Guardian.

            You analyse management meetings.

            Return ONLY structured JSON.

            Never return markdown.

            Never return code fences.

            Never explain your reasoning.

            Follow the supplied schema exactly.
            """,
              prompt);

        return result ??
               new MeetingAnalysisResponse();
    }

    private static string BuildPrompt(
    string meetingNotes,
    NekomataWorkspace workspace)
    {
        var projects =
     workspace.Projects.Count == 0
         ? "None"
         : string.Join(
             Environment.NewLine + Environment.NewLine,
             workspace.Projects.Select(p => $"""
Project ID: {p.Id}
Name: {p.Name}
Status: {p.Status}
Priority: {p.Priority}
Progress: {p.ProgressPercent}%
Business Value: {p.EstimatedBusinessValue:C0}
Due: {p.DueAt?.ToString("dd MMM yyyy") ?? "None"}
Next Action: {p.NextAction ?? "None"}
"""));

        var tasks =
        workspace.Tasks.Count == 0
            ? "None"
            : string.Join(
                Environment.NewLine + Environment.NewLine,
                workspace.Tasks.Select(t => $"""
Task ID: {t.Id}
Title: {t.Title}
Priority: {t.Priority}
Business Value: {t.EstimatedBusinessValue:C0}
Due: {t.DueAt?.ToString("dd MMM yyyy") ?? "None"}
Project ID: {t.ProjectId?.ToString() ?? "None"}
"""));

        return $"""
You are Guardian.

You have been given:

- the current workspace
- all existing projects
- all existing tasks
- notes from a management meeting

Existing Projects

{projects}

Existing Tasks

{tasks}

Meeting Notes

{meetingNotes}

Your job is to determine:

1. Which existing projects should be updated.

2. Which existing tasks should be updated.

3. Which new tasks should be created.

4. Whether any projects should be reprioritised.

5. Whether any deadlines should change.

6. Whether any clarification questions must be asked.

Rules

- Never invent project names.
- Never invent task names.
- If the meeting references work that does not clearly belong to an existing project, ask a clarification question instead of inventing one.
- Base every action on the supplied workspace and meeting notes.
- Prefer updating existing work over creating duplicates.
- Be commercially aware.
- Assume the user is an IT Manager managing multiple concurrent projects.
""";
    }
}