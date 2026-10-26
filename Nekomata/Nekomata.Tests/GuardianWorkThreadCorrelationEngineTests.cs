using Nekomata.Core.Guardian.WorkThreads;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianWorkThreadCorrelationEngineTests
{
    [Fact]
    public async Task Explicit_project_task_and_open_loop_links_are_confirmed_at_100_percent()
    {
        var repository = new FakeThreads();
        var loop = new GuardianOpenLoop { Id = Guid.NewGuid(), ProjectId = 7, SourceEventId = Guid.NewGuid() };
        var workspace = Workspace();
        var engine = new GuardianWorkThreadCorrelationEngine(repository, new FakeLoops(loop));

        await engine.CorrelateAsync(workspace);

        Assert.Contains(repository.Links, x => x.EntityType == "Project" && x.Confidence == 100 && x.Authority == WorkThreadLinkAuthority.Explicit && x.Status == WorkThreadLinkStatus.Confirmed);
        Assert.Contains(repository.Links, x => x.EntityType == "Task" && x.Confidence == 100 && x.LinkMethod == "Task.ProjectId");
        Assert.Contains(repository.Links, x => x.EntityType == "OpenLoop" && x.Confidence == 100 && x.RelationshipType == "Blocks");
    }

    [Fact]
    public async Task Exact_external_id_in_linked_task_confirms_external_signal()
    {
        var repository = new FakeThreads();
        var workspace = Workspace();
        workspace.Tasks[0].Description = "Implement configuration from request #5721";
        workspace.IntegrationMissionCandidates.Add(new MissionCandidate
        {
            SourceType = "Planner", SourceRecordId = "5721", Title = "Web chat configuration"
        });

        await new GuardianWorkThreadCorrelationEngine(repository, new FakeLoops()).CorrelateAsync(workspace);

        var link = Assert.Single(repository.Links, x => x.EntityType == "Planner");
        Assert.Equal(WorkThreadLinkStatus.Confirmed, link.Status);
        Assert.True(link.Confidence >= 95);
        Assert.Contains(repository.Evidence, x => x.LinkId == link.Id && x.EvidenceType == "ExactExternalId" && x.Weight == 95);
        Assert.Empty(workspace.UnboundContext);
    }

    [Fact]
    public async Task Project_name_only_correlation_remains_candidate_and_unbound()
    {
        var repository = new FakeThreads();
        var workspace = Workspace();
        workspace.IntegrationMissionCandidates.Add(new MissionCandidate
        {
            SourceType = "CalendarEvent", SourceRecordId = "event-1", Title = "Web Chat change control"
        });

        await new GuardianWorkThreadCorrelationEngine(repository, new FakeLoops()).CorrelateAsync(workspace);

        var link = Assert.Single(repository.Links, x => x.EntityType == "CalendarEvent");
        Assert.Equal(80, link.Confidence);
        Assert.Equal(WorkThreadLinkStatus.Candidate, link.Status);
        Assert.Equal(WorkThreadLinkAuthority.Deterministic, link.Authority);
        Assert.Equal("Correlation exists but requires confirmation.", Assert.Single(workspace.UnboundContext).Reason);
    }

    [Fact]
    public async Task Independent_persisted_evidence_promotes_candidate_but_duplicate_family_does_not_inflate()
    {
        var repository = new FakeThreads();
        var workspace = Workspace();
        workspace.IntegrationMissionCandidates.Add(new MissionCandidate
        {
            SourceType = "TeamsMessage", SourceRecordId = "message-1", Title = "Web Chat update"
        });
        var engine = new GuardianWorkThreadCorrelationEngine(repository, new FakeLoops());
        await engine.CorrelateAsync(workspace);
        var link = Assert.Single(repository.Links, x => x.EntityType == "TeamsMessage");
        Assert.Equal(80, link.Confidence);

        repository.Evidence.Add(new GuardianCorrelationEvidence
        {
            LinkId = link.Id, EvidenceType = "KnownStakeholder", Value = "Craig", Weight = 20,
            SourceEventId = Guid.NewGuid()
        });
        await engine.CorrelateAsync(workspace);

        Assert.Equal(100, link.Confidence);
        Assert.Equal(WorkThreadLinkStatus.Confirmed, link.Status);
        Assert.Contains("ExactProjectName (80)", link.LinkMethod);
        Assert.Contains("KnownStakeholder (20)", link.LinkMethod);
    }

    private static NekomataWorkspace Workspace() => new()
    {
        Projects = [new NekomataProject { Id = 7, Name = "Web Chat" }],
        Tasks = [new NekomataTask { Id = 12, ProjectId = 7, Title = "Complete Web Chat change control" }]
    };

    private sealed class FakeLoops(params GuardianOpenLoop[] loops) : IGuardianOpenLoopRepository
    {
        public Task<GuardianOpenLoop> UpsertAsync(GuardianOpenLoop loop) => Task.FromResult(loop);
        public Task<List<GuardianOpenLoop>> GetOpenAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<List<GuardianOpenLoop>> GetAwaitingAsync(int count = 200) => Task.FromResult(loops.ToList());
        public Task<bool> ResolveAsync(Guid id, GuardianLoopEvidence evidence) => Task.FromResult(false);
    }

    private sealed class FakeThreads : IGuardianWorkThreadRepository
    {
        public List<GuardianWorkThread> Threads { get; } = [];
        public List<GuardianWorkThreadLink> Links { get; } = [];
        public List<GuardianCorrelationEvidence> Evidence { get; } = [];
        public Task<GuardianWorkThread> GetOrCreateForProjectAsync(long projectId, string title, Guid sourceEventId)
        {
            var thread = Threads.FirstOrDefault(x => x.PrimaryProjectId == projectId) ?? new GuardianWorkThread { Id = Guid.NewGuid(), PrimaryProjectId = projectId, Title = title };
            if (!Threads.Contains(thread)) Threads.Add(thread);
            return Task.FromResult(thread);
        }
        public Task<GuardianWorkThreadLink> UpsertLinkAsync(GuardianWorkThreadLink link)
        {
            var existing = Links.FirstOrDefault(x => x.ThreadId == link.ThreadId && x.EntityType == link.EntityType && x.EntityKey == link.EntityKey);
            if (existing is not null) return Task.FromResult(existing);
            Links.Add(link); return Task.FromResult(link);
        }
        public Task AddEvidenceAsync(GuardianCorrelationEvidence evidence) { Evidence.Add(evidence); return Task.CompletedTask; }
        public Task<List<GuardianCorrelationEvidence>> GetEvidenceAsync(Guid linkId) => Task.FromResult(Evidence.Where(x => x.LinkId == linkId).ToList());
        public Task UpdateEvaluationAsync(Guid linkId, int confidence, WorkThreadLinkStatus status, string method)
        {
            var link = Links.Single(x => x.Id == linkId);
            if (link.Authority != WorkThreadLinkAuthority.Explicit)
            {
                link.Confidence = confidence; link.Status = status; link.LinkMethod = method;
            }
            return Task.CompletedTask;
        }
        public Task<GuardianWorkThreadLink?> FindLinkAsync(string type, string key, WorkThreadLinkStatus? status = null) =>
            Task.FromResult(Links.Where(x => x.EntityType == type && x.EntityKey == key && (status is null || x.Status == status)).OrderByDescending(x => x.Confidence).FirstOrDefault());
        public Task<List<GuardianWorkThread>> GetActiveAsync()
        {
            foreach (var thread in Threads) thread.Links = Links.Where(x => x.ThreadId == thread.Id).ToList();
            return Task.FromResult(Threads.ToList());
        }
    }
}
