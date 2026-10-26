using Nekomata.Data.Repositories;
using Nekomata.Models.Analytics;
using Nekomata.Models.Missions;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;

namespace Nekomata.Core.Analytics;

public enum ValueEvidenceKind { Measured, Estimated, Proxy, NonFinancial }

public sealed record WorkEpisode(
    string Key, string Title, string Category, DateTime StartedAt, DateTime FinishedAt,
    int ActiveMinutes, int AdditionalMinutes, string Outcome, decimal FinancialValue,
    ValueEvidenceKind ValueEvidence, int ValueConfidence, string StrategicRelevance,
    int LeverageScore, string OperatingMode, string Cluster, long? ProjectId, long? TaskId,
    IReadOnlyList<string> Evidence);

public sealed record InvestmentArea(
    string Name, int Minutes, int AdditionalMinutes, decimal AssociatedFinancialValue,
    int EpisodeCount, string ValueProfile)
{
    public double CapacityPercent { get; init; }
}

public sealed record EpisodeCluster(string Name, int EpisodeCount, int Minutes, int AdditionalMinutes,
    decimal AssociatedFinancialValue, int AverageLeverageScore);

public sealed record LeverageBand(string Name, int Minutes, int AdditionalMinutes, double CapacityPercent);

public sealed record ExecutiveValueSummary(
    DateTime From, DateTime Through, int TotalMinutes, int AdditionalMinutes,
    decimal LabourCost, decimal AssociatedFinancialValue, IReadOnlyList<InvestmentArea> Areas,
    IReadOnlyList<WorkEpisode> Episodes, IReadOnlyList<EpisodeCluster> Clusters,
    IReadOnlyList<LeverageBand> LeverageBands, IReadOnlyList<string> Judgements)
{
    public decimal ValuePerHour => TotalMinutes > 0 ? Math.Round(AssociatedFinancialValue / (TotalMinutes / 60m), 2) : 0;
    public int MeasuredCount => Episodes.Count(x => x.ValueEvidence == ValueEvidenceKind.Measured);
    public int EstimatedCount => Episodes.Count(x => x.ValueEvidence == ValueEvidenceKind.Estimated);
    public int ProxyCount => Episodes.Count(x => x.ValueEvidence == ValueEvidenceKind.Proxy);
    public int NonFinancialCount => Episodes.Count(x => x.ValueEvidence == ValueEvidenceKind.NonFinancial);
    public int HighStrategicRelevanceCount => Episodes.Count(x => x.StrategicRelevance == "High");
    public int AverageLeverageScore => Episodes.Count == 0 ? 0 : (int)Math.Round(Episodes.Average(x => x.LeverageScore));
    public WorkEpisode? HighestLeverageEpisode => Episodes.OrderByDescending(x => x.LeverageScore).ThenByDescending(x => x.FinancialValue).FirstOrDefault();
}

public sealed class ExecutiveValueEngine(
    IMissionSessionRepository sessions,
    IProjectRepository projects,
    ITaskRepository tasks,
    WeeklyOvertimeService overtime,
    LabourCostSettings costing)
{
    public async Task<ExecutiveValueSummary> BuildAsync(DateTime from, DateTime through)
    {
        var history = (await sessions.GetBetweenAsync(from, through))
            .Where(x => !x.Cancelled && x.ActualDurationMinutes > 0).ToList();
        var projectMap = (await projects.GetAllAsync()).ToDictionary(x => x.Id);
        var openTasks = await tasks.GetOpenTasksAsync();
        return Analyse(history, projectMap, from, through,
            session => overtime.GetAdditionalMinutes(session, from, through), costing.HourlyLabourRate, openTasks);
    }

    public static ExecutiveValueSummary Analyse(
        IReadOnlyCollection<MissionSession> history,
        IReadOnlyDictionary<long, NekomataProject> projects,
        DateTime from, DateTime through,
        Func<MissionSession, int> additionalMinutes,
        decimal hourlyRate,
        IReadOnlyCollection<NekomataTask>? openTasks = null)
    {
        var episodes = history.Select(session => BuildEpisode(session,
                session.ProjectId is long id && projects.TryGetValue(id, out var project) ? project : null,
                additionalMinutes(session)))
            .OrderBy(x => x.StartedAt).ToList();
        var totalMinutes = episodes.Sum(x => x.ActiveMinutes);
        var financialValue = DeduplicatedValue(episodes);
        var areas = episodes.GroupBy(x => x.Category).Select(group => new InvestmentArea(
                group.Key, group.Sum(x => x.ActiveMinutes), group.Sum(x => x.AdditionalMinutes),
                DeduplicatedValue(group), group.Count(), DescribeValueProfile(group))
            { CapacityPercent = totalMinutes == 0 ? 0 : Math.Round(group.Sum(x => x.ActiveMinutes) * 100d / totalMinutes, 1) })
            .OrderByDescending(x => x.Minutes).ToList();
        var clusters = episodes.GroupBy(x => x.Cluster).Select(group => new EpisodeCluster(
                group.Key, group.Count(), group.Sum(x => x.ActiveMinutes), group.Sum(x => x.AdditionalMinutes),
                DeduplicatedValue(group), (int)Math.Round(group.Average(x => x.LeverageScore))))
            .OrderByDescending(x => x.Minutes).ToList();
        var leverageBands = episodes.GroupBy(x => x.OperatingMode).Select(group => new LeverageBand(
                group.Key, group.Sum(x => x.ActiveMinutes), group.Sum(x => x.AdditionalMinutes),
                totalMinutes == 0 ? 0 : Math.Round(group.Sum(x => x.ActiveMinutes) * 100d / totalMinutes, 1)))
            .OrderByDescending(x => x.Minutes).ToList();
        var judgements = BuildJudgements(episodes, clusters, openTasks ?? []);
        return new ExecutiveValueSummary(from, through, totalMinutes, episodes.Sum(x => x.AdditionalMinutes),
            Math.Round(totalMinutes / 60m * Math.Max(0, hourlyRate), 2, MidpointRounding.AwayFromZero),
            financialValue, areas, episodes, clusters, leverageBands, judgements);
    }

    private static WorkEpisode BuildEpisode(MissionSession session, NekomataProject? project, int additionalMinutes)
    {
        var text = $"{session.Title} {session.SourceType} {project?.Name} {project?.Description}".ToLowerInvariant();
        var category = ClassifyCategory(text);
        var nonFinancial = ContainsAny(text, "compliance", "risk", "governance", "change control");
        var evidence = session.BusinessValue > 0 ? ValueEvidenceKind.Estimated : nonFinancial ? ValueEvidenceKind.NonFinancial : ValueEvidenceKind.Proxy;
        var valueConfidence = evidence switch
        {
            ValueEvidenceKind.Measured => 90,
            ValueEvidenceKind.Estimated when project is not null => 60,
            ValueEvidenceKind.Estimated => 45,
            ValueEvidenceKind.NonFinancial => 70,
            _ => 40
        };
        var relevance = project?.Priority.Equals("Critical", StringComparison.OrdinalIgnoreCase) == true ||
                        project?.Priority.Equals("High", StringComparison.OrdinalIgnoreCase) == true || session.BusinessValue >= 10000 || nonFinancial
            ? "High" : session.Score >= 70 || session.BusinessValue > 0 ? "Medium" : "Operational";
        var evidenceItems = new List<string>
        {
            $"Mission session {session.Id}: {session.StartedAt:g}–{session.FinishedAt:g}",
            $"Recorded active duration: {session.ActualDurationMinutes} minutes"
        };
        if (project is not null) evidenceItems.Add($"Project: {project.Name} · {project.Priority} priority");
        if (session.BusinessValue > 0) evidenceItems.Add($"Estimated business value attached to source work: {session.BusinessValue:C0}");
        var (leverage, mode) = AssessLeverage(text, category, session);
        var cluster = Cluster(text, category);
        return new WorkEpisode(session.Id > 0 ? $"mission:{session.Id}" : $"mission:{session.StartedAt:O}:{session.Title}",
            session.Title, category, session.StartedAt, session.FinishedAt, session.ActualDurationMinutes,
            Math.Min(session.ActualDurationMinutes, Math.Max(0, additionalMinutes)),
            session.Completed ? "Completed" : "Deferred", session.BusinessValue, evidence, valueConfidence, relevance,
            leverage, mode, cluster,
            session.ProjectId, session.TaskId, evidenceItems);
    }

    private static decimal DeduplicatedValue(IEnumerable<WorkEpisode> episodes) => episodes
        .Where(x => x.ValueEvidence is ValueEvidenceKind.Measured or ValueEvidenceKind.Estimated && x.FinancialValue > 0)
        .GroupBy(x => x.ProjectId is long projectId ? $"project:{projectId}" : x.TaskId is long taskId ? $"task:{taskId}" : x.Key)
        .Sum(group => group.Max(x => x.FinancialValue));

    private static string DescribeValueProfile(IEnumerable<WorkEpisode> episodes)
    {
        var kinds = episodes.GroupBy(x => x.ValueEvidence).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} {x.Count()}");
        return string.Join(" · ", kinds);
    }

    private static string ClassifyCategory(string text)
    {
        if (ContainsAny(text, "compliance", "risk", "audit", "incident")) return "Operational resilience";
        if (ContainsAny(text, "report", "analysis", "data", "sql", "forecast", "yoy")) return "Reporting & data";
        if (ContainsAny(text, "support", "request", "query", "fix")) return "Operational support";
        if (ContainsAny(text, "team meeting", "team leadership", "1:1", "one to one", "people management",
                "performance review", "coach", "mentoring")) return "Team management";
        if (ContainsAny(text, "email", "reply", "admin", "chase")) return "Administration & communication";
        if (ContainsAny(text, "project", "strategy", "programme", "implementation", "design")) return "Strategic projects";
        return "General delivery";
    }

    private static (int Score, string Mode) AssessLeverage(string text, string category, MissionSession session)
    {
        if (ContainsAny(text, "rework", "redo", "correction", "avoidable", "duplicate")) return (10, "Rework / avoidable");
        if (ContainsAny(text, "automation", "automate", "self-service", "process improvement")) return (90, "High-leverage work");
        if (ContainsAny(text, "delegate", "training", "documentation", "runbook", "standardise")) return (78, "High-leverage work");
        if (category is "Administration & communication") return (25, "Administrative overhead");
        if (category is "Operational support" or "Operational resilience")
            return (35, "Reactive work");
        if (session.ProjectId is not null || category == "Strategic projects") return (68, "High-leverage work");
        return (52, "Direct delivery");
    }

    private static string Cluster(string text, string category)
    {
        if (ContainsAny(text, "report", "analysis", "forecast", "yoy")) return "Reporting & analysis";
        return category;
    }

    private static List<string> BuildJudgements(
        IReadOnlyList<WorkEpisode> episodes,
        IReadOnlyList<EpisodeCluster> clusters,
        IReadOnlyCollection<NekomataTask> openTasks)
    {
        var result = new List<string>();
        var highestAlternative = openTasks.OrderByDescending(TaskOpportunityScore).FirstOrDefault();
        var lowLeverage = episodes.Where(x => x.LeverageScore < 40 && x.ActiveMinutes >= 30)
            .OrderByDescending(x => x.ActiveMinutes).FirstOrDefault();
        if (lowLeverage is not null && highestAlternative is not null)
            result.Add($"Opportunity cost: {lowLeverage.Title} consumed {lowLeverage.ActiveMinutes} low-leverage minutes while " +
                       $"{highestAlternative.Title} remained open. This is a capacity comparison, not proof of direct displacement.");

        var repeat = clusters.Where(x => x.EpisodeCount >= 3 && x.AverageLeverageScore < 50)
            .OrderByDescending(x => x.Minutes).FirstOrDefault();
        if (repeat is not null)
            result.Add($"Delegation opportunity: {repeat.Name} consumed {WeeklyOvertimeSummary.FormatMinutes(repeat.Minutes)} across " +
                       $"{repeat.EpisodeCount} episodes. Review it for team ownership, automation or a documented process.");

        var total = episodes.Sum(x => x.ActiveMinutes);
        var structural = episodes.Where(x => x.OperatingMode is "Reactive work" or "Administrative overhead" or "Rework / avoidable").ToList();
        var structuralMinutes = structural.Sum(x => x.ActiveMinutes);
        var structuralOvertime = structural.Sum(x => x.AdditionalMinutes);
        if (total > 0 && structuralOvertime >= 30 && structuralMinutes * 100d / total >= 35)
            result.Add($"Structural overload signal: reactive, administrative or avoidable work used {structuralMinutes * 100d / total:0}% of recorded time and " +
                       $"caused {WeeklyOvertimeSummary.FormatMinutes(structuralOvertime)} additional time. Treat this as a system-design issue, not a personal productivity failure.");
        return result;
    }

    private static double TaskOpportunityScore(NekomataTask task) =>
        task.PriorityScore + task.ExecutiveVisibility * 10 + task.AutomationPotential * 5 +
        (double)Math.Min(task.EstimatedBusinessValue / 1000m, 100m);

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
}
