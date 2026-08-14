using Nekomata.Models.AI;
using Nekomata.Models.Business;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Briefing;
namespace Nekomata.Models.Workspace;
using Nekomata.Models.Decision;
using Nekomata.Models.Missions;
using Nekomata.Models.Guardian;
using Nekomata.Models.Integrations;
using Nekomata.Models.Planning;
public class NekomataWorkspace
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public List<NekomataTask> Tasks { get; set; } = [];
    public List<NekomataProject> Projects { get; set; } = [];
    public List<BusinessMetric> BusinessMetrics { get; set; } = [];

    public FocusSummary Focus { get; set; } = new();
    public CapacitySummary Capacity { get; set; } = new();
    public AiRecommendation AiRecommendation { get; set; } = new();

    public WorkspaceState State { get; set; } = new();

    public MorningBriefing Briefing { get; set; } = new();

    public DecisionRecommendation Recommendation { get; set; } = new();

    public Mission CurrentMission { get; set; } = new();

    public GuardianState Guardian { get; set; } = new();

    public List<MissionCandidate> RankedMissionCandidates { get; set; } = [];

    public GuardianEvidence GuardianEvidence { get; set; }
    = new();
    public List<MissionCandidate> IntegrationMissionCandidates { get; set; }
    = [];
    public List<IntegrationStatus> Integrations { get; set; }
    = [];
    public List<MissionTimelineItem> Timeline { get; set; }
    = [];
    public List<GuardianInsight> Insights { get; set; }
    = [];
}