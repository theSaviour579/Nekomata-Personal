using System.Security.Cryptography;
using System.Text;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.WorkThreads;

public sealed class GuardianWorkThreadCorrelationEngine(
    IGuardianWorkThreadRepository threads,
    IGuardianOpenLoopRepository openLoops)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    { "project", "review", "change", "control", "task", "work", "new", "the", "and", "for", "with" };

    public async Task CorrelateAsync(NekomataWorkspace workspace)
    {
        var byProject = new Dictionary<long, GuardianWorkThread>();
        foreach (var project in workspace.Projects)
        {
            var sourceEvent = StableId("project", project.Id.ToString());
            var thread = await threads.GetOrCreateForProjectAsync(project.Id, project.Name, sourceEvent);
            byProject[project.Id] = thread;
            await LinkAsync(thread.Id, "Project", project.Id.ToString(), "BelongsTo", 100,
                WorkThreadLinkAuthority.Explicit, "PrimaryProject", WorkThreadLinkStatus.Confirmed, sourceEvent,
                [("ExplicitProjectId", project.Id.ToString(), 100)]);
        }

        foreach (var task in workspace.Tasks.Where(x => x.ProjectId is not null && byProject.ContainsKey(x.ProjectId.Value)))
        {
            var sourceEvent = StableId("task", task.Id.ToString());
            await LinkAsync(byProject[task.ProjectId!.Value].Id, "Task", task.Id.ToString(), "BelongsTo", 100,
                WorkThreadLinkAuthority.Explicit, "Task.ProjectId", WorkThreadLinkStatus.Confirmed, sourceEvent,
                [("ExplicitProjectId", task.ProjectId.Value.ToString(), 100)]);
        }

        foreach (var loop in (await openLoops.GetOpenAsync()).Where(x => x.ProjectId is not null && byProject.ContainsKey(x.ProjectId.Value)))
        {
            await LinkAsync(byProject[loop.ProjectId!.Value].Id, "OpenLoop", loop.Id.ToString(), "Blocks", 100,
                WorkThreadLinkAuthority.Explicit, "OpenLoop.ProjectId", WorkThreadLinkStatus.Confirmed, loop.SourceEventId,
                [("ExplicitProjectId", loop.ProjectId.Value.ToString(), 100)]);
        }

        foreach (var external in workspace.IntegrationMissionCandidates)
        {
            var entityType = external.SourceType;
            var entityKey = external.SourceRecordId ?? external.Title;
            if (string.IsNullOrWhiteSpace(entityKey)) continue;
            foreach (var project in workspace.Projects)
            {
                var evidence = ScoreExternal(external.Title + " " + external.Description, entityKey, project.Id, project.Name, workspace);
                if (evidence.Count == 0) continue;
                var score = Math.Min(100, evidence.Sum(x => x.Weight));
                var status = score >= 90 ? WorkThreadLinkStatus.Confirmed : WorkThreadLinkStatus.Candidate;
                var sourceEvent = StableId(entityType, entityKey);
                await LinkAsync(byProject[project.Id].Id, entityType, entityKey, "Discusses", score,
                    WorkThreadLinkAuthority.Deterministic, string.Join(" + ", evidence.Select(x => x.Type)),
                    status, sourceEvent, evidence.Select(x => (x.Type, x.Value, x.Weight)).ToList());
            }
        }

        workspace.WorkThreads = await threads.GetActiveAsync();
        workspace.UnboundContext = [];
        foreach (var external in workspace.IntegrationMissionCandidates)
        {
            var key = external.SourceRecordId ?? external.Title;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (await threads.FindLinkAsync(external.SourceType, key, WorkThreadLinkStatus.Confirmed) is not null) continue;
            workspace.UnboundContext.Add(new GuardianUnboundContext
            {
                EntityType = external.SourceType, EntityKey = key, Title = external.Title,
                Reason = await threads.FindLinkAsync(external.SourceType, key) is null
                    ? "No correlation evidence found." : "Correlation exists but requires confirmation."
            });
        }
    }

    private static List<(string Type, string Value, int Weight)> ScoreExternal(
        string text, string externalId, long projectId, string projectName, NekomataWorkspace workspace)
    {
        var evidence = new List<(string Type, string Value, int Weight)>();
        var linkedTasks = workspace.Tasks.Where(x => x.ProjectId == projectId).ToList();
        if (linkedTasks.Any(x => ($"{x.Title} {x.Description}").Contains(externalId, StringComparison.OrdinalIgnoreCase)))
            evidence.Add(("ExactExternalId", externalId, 95));

        if (text.Contains(projectName, StringComparison.OrdinalIgnoreCase))
            evidence.Add(("ExactProjectName", projectName, 80));
        else
        {
            var terms = DistinctiveTerms(projectName);
            var matches = terms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (matches >= 2) evidence.Add(("DistinctiveTerms", string.Join(", ", terms.Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))), 65));
        }
        return evidence;
    }

    private async Task LinkAsync(Guid threadId, string entityType, string entityKey, string relationship,
        int confidence, WorkThreadLinkAuthority authority, string method, WorkThreadLinkStatus status,
        Guid sourceEventId, IReadOnlyCollection<(string Type, string Value, int Weight)> evidence)
    {
        var link = await threads.UpsertLinkAsync(new GuardianWorkThreadLink
        {
            ThreadId = threadId, EntityType = entityType, EntityKey = entityKey,
            RelationshipType = relationship, Confidence = confidence, Authority = authority,
            LinkMethod = method, Status = status, SourceEventId = sourceEventId
        });
        foreach (var item in evidence)
            await threads.AddEvidenceAsync(new GuardianCorrelationEvidence
            {
                LinkId = link.Id, EvidenceType = item.Type, Value = item.Value,
                Weight = item.Weight, SourceEventId = sourceEventId
            });
        if (authority != WorkThreadLinkAuthority.Explicit)
        {
            var evaluation = GuardianCorrelationPolicy.Evaluate(await threads.GetEvidenceAsync(link.Id));
            await threads.UpdateEvaluationAsync(link.Id, evaluation.Confidence, evaluation.Status, evaluation.Explanation);
        }
    }

    private static List<string> DistinctiveTerms(string value) => value.Split([' ', '-', '/', '&'], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => new string(x.Where(char.IsLetterOrDigit).ToArray())).Where(x => x.Length >= 4 && !StopWords.Contains(x))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Guid StableId(string type, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"work-thread:{type}:{key}"))[..16]);
}
