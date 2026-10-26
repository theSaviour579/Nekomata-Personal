using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Context;

public sealed class ProjectCommunicationLoopService(
    GuardianOpenLoopService openLoops,
    IGuardianOpenLoopRepository repository,
    IProjectRepository projects)
{
    private const string SourcePrefix = "ProjectCommunication:";

    public async Task AnalyseTeamsAsync(NekomataWorkspace workspace, IReadOnlyCollection<ContextSignal> messages)
    {
        var existing = (await SanitizeOpenLoopsAsync()).ToList();
        foreach (var loop in existing.Where(x => x.Type == OpenLoopType.AwaitingMe).ToList())
        {
            var source = messages.FirstOrDefault(x => x.ExternalId.Equals(loop.SourceExternalId, StringComparison.OrdinalIgnoreCase));
            if (source is not null && (source.Disposition != ContextSignalDisposition.Correlated ||
                !source.Status.Equals("Inbound", StringComparison.OrdinalIgnoreCase) ||
                !ProjectCommunicationPolicy.RequiresEndUserResponse(source.Body)))
            {
                await ResolveExplicitAsync(loop, DateTimeOffset.UtcNow,
                    StableEventId("project-loop-suppressed", loop.Id.ToString()), source.ExternalId,
                    "Guardian withdrew a low-confidence or machine-generated project response prompt.");
                existing.Remove(loop);
            }
        }
        var pendingProjects = existing.Where(x => x.Type == OpenLoopType.AwaitingMe && x.ProjectId is not null)
            .Select(x => x.ProjectId!.Value).ToHashSet();

        foreach (var message in messages.Where(x => x.WorkThreadId is not null &&
                     x.Disposition == ContextSignalDisposition.Correlated)
                     .OrderBy(x => x.ObservedAt))
        {
            var thread = workspace.WorkThreads.FirstOrDefault(x => x.Id == message.WorkThreadId);
            var project = thread?.PrimaryProjectId is long id
                ? workspace.Projects.FirstOrDefault(x => x.Id == id)
                : null;
            if (project is null || string.IsNullOrWhiteSpace(message.ConversationId)) continue;

            if (message.Status.Equals("Outbound", StringComparison.OrdinalIgnoreCase))
            {
                await ResolveResponseAsync(project.Id, message.ConversationId, message.ObservedAt,
                    message.SourceEventId, message.ExternalId, "Teams response sent");
                if (ProjectCommunicationPolicy.IndicatesExternalWait(message.Body))
                    await CaptureExternalWaitAsync(project.Id, project.Name, message.ConversationId,
                        message.ExternalId, message.SourceEventId, message.ObservedAt, message.Body, "Teams");
                continue;
            }

            if (message.Status.Equals("Inbound", StringComparison.OrdinalIgnoreCase) &&
                !pendingProjects.Contains(project.Id) &&
                ProjectCommunicationPolicy.RequiresEndUserResponse(message.Body))
            {
                await openLoops.CaptureAsync(new GuardianOpenLoop
                {
                    CreatedAt = message.ObservedAt,
                    UpdatedAt = message.ObservedAt,
                    Type = OpenLoopType.AwaitingMe,
                    Title = $"Update the user · {project.Name}",
                    Detail = $"Teams update from {message.Person}: {Excerpt(message.Body)}",
                    WaitingOn = message.Person,
                    DueAt = ProjectCommunicationPolicy.NextWorkingMorning(message.ObservedAt),
                    ProjectId = project.Id,
                    TeamsMessageId = message.ExternalId,
                    Source = SourcePrefix + message.ConversationId,
                    SourceExternalId = message.ExternalId,
                    SourceEventId = message.SourceEventId,
                    ExpectedEvidenceKind = "ProjectResponseSent"
                });
                pendingProjects.Add(project.Id);
            }
        }
    }

    public async Task<IReadOnlyList<GuardianOpenLoop>> SanitizeOpenLoopsAsync()
    {
        var existing = (await repository.GetOpenAsync()).Where(IsProjectCommunication).ToList();
        foreach (var loop in existing.Where(x => x.Type == OpenLoopType.AwaitingMe &&
                     !ProjectCommunicationPolicy.RequiresEndUserResponse(x.Detail)).ToList())
        {
            await ResolveExplicitAsync(loop, DateTimeOffset.UtcNow,
                StableEventId("project-loop-content-suppressed", loop.Id.ToString()), loop.SourceExternalId,
                "Guardian withdrew a persisted response prompt whose evidence does not contain an explicit end-user handoff.");
            existing.Remove(loop);
        }

        foreach (var group in existing.Where(x => x.Type == OpenLoopType.AwaitingMe && x.ProjectId is not null)
                     .GroupBy(x => x.ProjectId!.Value))
        {
            var retained = group.OrderByDescending(x => x.CreatedAt).First();
            foreach (var duplicate in group.Where(x => x.Id != retained.Id).ToList())
            {
                await ResolveExplicitAsync(duplicate, DateTimeOffset.UtcNow,
                    StableEventId("project-loop-duplicate", duplicate.Id.ToString()), duplicate.SourceExternalId,
                    "Guardian consolidated duplicate response prompts for this project.");
                existing.Remove(duplicate);
            }
        }
        return existing;
    }

    public async Task AnalyseSentMailAsync(IEnumerable<EmailMessage> messages)
    {
        var pending = (await repository.GetOpenAsync()).Where(IsProjectCommunication).ToList();
        if (pending.Count == 0) return;
        var projectList = await projects.GetAllAsync();
        foreach (var message in messages.OrderBy(x => x.ReceivedAt))
        {
            var text = $"{message.Subject} {message.BodyPreview}";
            foreach (var loop in pending.Where(x => x.Type == OpenLoopType.AwaitingMe && x.CreatedAt < message.ReceivedAt).ToList())
            {
                var project = projectList.FirstOrDefault(x => x.Id == loop.ProjectId);
                if (project is null || !MatchesProject(text, project.Name, loop.Detail)) continue;
                await ResolveExplicitAsync(loop, message.ReceivedAt, StableEventId("project-mail", message.Id),
                    message.Id, $"Sent email matched {project.Name}: {message.Subject}");
                pending.Remove(loop);
                if (ProjectCommunicationPolicy.IndicatesExternalWait(text))
                    await CaptureExternalWaitAsync(project.Id, project.Name, message.ConversationId,
                        message.Id, StableEventId("project-mail-wait", message.Id), message.ReceivedAt,
                        text, "Outlook");
            }
        }
    }

    public static bool IsProjectCommunication(GuardianOpenLoop loop) =>
        loop.Source.StartsWith(SourcePrefix, StringComparison.OrdinalIgnoreCase);

    private async Task ResolveResponseAsync(long projectId, string conversationId, DateTimeOffset at,
        Guid eventId, string externalId, string summary)
    {
        var source = SourcePrefix + conversationId;
        foreach (var loop in (await repository.GetOpenAsync()).Where(x => x.ProjectId == projectId &&
                     x.Type == OpenLoopType.AwaitingMe && x.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                     x.CreatedAt < at))
            await ResolveExplicitAsync(loop, at, eventId, externalId, summary);
    }

    private async Task ResolveExplicitAsync(GuardianOpenLoop loop, DateTimeOffset at, Guid eventId,
        string externalId, string summary) => await openLoops.ResolveFromEvidenceAsync(new GuardianLoopEvidence
    {
        EventId = eventId,
        OpenLoopId = loop.Id,
        OccurredAt = at,
        Kind = "ProjectResponseSent",
        Summary = summary,
        ProjectId = loop.ProjectId,
        SourceExternalId = externalId
    });

    private async Task CaptureExternalWaitAsync(long projectId, string projectName, string conversationId,
        string externalId, Guid eventId, DateTimeOffset at, string detail, string channel) =>
        await openLoops.CaptureAsync(new GuardianOpenLoop
        {
            CreatedAt = at, UpdatedAt = at, Type = OpenLoopType.AwaitingThem,
            Title = $"External dependency · {projectName}",
            Detail = $"{channel} evidence indicates the next action is external: {Excerpt(detail)}",
            WaitingOn = "External party", ProjectId = projectId,
            DueAt = ProjectCommunicationPolicy.NextWorkingMorning(at).AddDays(1),
            Source = SourcePrefix + (string.IsNullOrWhiteSpace(conversationId) ? "mail" : conversationId),
            SourceExternalId = "wait:" + externalId, SourceEventId = eventId,
            ExpectedEvidenceKind = "ExternalResponseReceived"
        });

    private static bool MatchesProject(string text, string projectName, string context)
    {
        var terms = Terms(projectName).ToList();
        if (terms.Count == 0) return false;
        var content = Terms(text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (terms.All(content.Contains)) return true;
        var contextTerms = Terms(context).Where(x => x.Length >= 5).ToList();
        return terms.Any(content.Contains) && contextTerms.Count(x => content.Contains(x)) >= 2;
    }

    private static IEnumerable<string> Terms(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value ?? "", "[A-Za-z0-9]+")
            .Select(x => x.Value.ToLowerInvariant()).Where(x => x.Length >= 4 &&
                x is not ("project" or "update" or "reply" or "user" or "with" or "from" or "that" or "this"));
    private static string Excerpt(string value) => value.Length <= 240 ? value : value[..237] + "...";
    private static Guid StableEventId(string source, string id) => new(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}:{id}"))[..16]);
}
