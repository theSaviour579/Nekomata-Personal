using Nekomata.Core.Guardian.Reasoning;
using Nekomata.Core.Missions.Candidates;
using Nekomata.Core.Missions.Scoring;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions;

public class MissionSelector : IMissionSelector
{
    private readonly IEnumerable<IMissionCandidateProvider>
        _candidateProviders;

    private readonly IMissionCandidateScorer
        _scorer;

    private readonly GuardianReasoningEngine
        _reasoningEngine;

    public MissionSelector(
        IEnumerable<IMissionCandidateProvider> candidateProviders,
        IMissionCandidateScorer scorer,
        GuardianReasoningEngine reasoningEngine)
    {
        _candidateProviders =
            candidateProviders;

        _scorer =
            scorer;

        _reasoningEngine =
            reasoningEngine;
    }

    public MissionCandidate? Select(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        return BuildRankedCandidates(workspace)
            .FirstOrDefault();
    }

    public IReadOnlyList<MissionCandidate> Rank(
        NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var ranked =
            BuildRankedCandidates(workspace);

        for (var index = 0;
             index < ranked.Count;
             index++)
        {
            ranked[index].Rank =
                index + 1;
        }

        return ranked;
    }

    private List<MissionCandidate> BuildRankedCandidates(
        NekomataWorkspace workspace)
    {
        var candidates =
            _candidateProviders
                .SelectMany(provider =>
                    provider.GetCandidates(workspace))
                .ToList();

        foreach (var candidate in candidates)
        {
            _scorer.Score(candidate);

            if (candidate.Title.Contains(
        "DRP for Station Road",
        StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine(
      $"{candidate.SourceType} | " +
      $"{candidate.Title} | " +
      $"Project={candidate.ProjectId} | " +
      $"Task={candidate.TaskId}");

                foreach (var factor in candidate.ScoreFactors)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"  Factor: {factor.Category} = " +
                        $"{factor.Points} | {factor.Explanation}");
                }
            }

            candidate.GuardianReasons.Clear();

            candidate.GuardianReasons.AddRange(
                _reasoningEngine.BuildReasons(
                    candidate));

            candidate.RecommendationReason =
                candidate.GuardianReasons.Count > 0
                    ? string.Join(
                        " ",
                        candidate.GuardianReasons
                            .Select(reason =>
                                reason.Explanation))
                    : BuildFallbackReason(
                        candidate);
        }

        // Deferred work must never displace actionable work. Only enter the
        // ranking when every normal candidate has been exhausted. Prefer items
        // that have remained unchanged for at least fourteen days; otherwise
        // expose all held work as the final fallback.
        var actionableCandidates = candidates
            .Where(candidate => !candidate.IsOnHold)
            .ToList();
        if (actionableCandidates.Count > 0)
        {
            candidates = actionableCandidates;
        }
        else
        {
            var heldCandidates = candidates
                .Where(candidate => candidate.IsOnHold)
                .ToList();
            var staleCutoff = DateTime.Now.AddDays(-14);
            var staleHeldCandidates = heldCandidates
                .Where(candidate => candidate.LastUpdatedAt is DateTime updated && updated <= staleCutoff)
                .ToList();
            candidates = staleHeldCandidates.Count > 0
                ? staleHeldCandidates
                : heldCandidates;

            foreach (var candidate in candidates)
            {
                candidate.RecommendationReason =
                    "All actionable work is complete. This item is on hold" +
                    (candidate.LastUpdatedAt is DateTime updated && updated <= staleCutoff
                        ? $" and has had no recorded change since {updated:dd MMM}."
                        : "; consider reviewing whether it can resume.");
            }
        }
        var actionableProjectIds =
            candidates
                .Where(candidate =>
                    string.Equals(
                        candidate.SourceType,
                        "Task",
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    candidate.ProjectId is not null)
                .Select(candidate =>
                    candidate.ProjectId!.Value)
                .ToHashSet();

        var filteredCandidates =
            candidates
                .Where(candidate =>
                    ShouldKeepCandidate(
                        candidate,
                        actionableProjectIds))
                .OrderByDescending(candidate =>
                    candidate.Score)
                .ThenByDescending(candidate =>
                    candidate.AtRisk)
                .ThenBy(candidate =>
                    candidate.DueAt
                    ?? DateTime.MaxValue)
                .ThenByDescending(candidate =>
                    candidate.BusinessValue)
                .ToList();

        return filteredCandidates;
    }

    private static bool ShouldKeepCandidate(
        MissionCandidate candidate,
        IReadOnlySet<long> actionableProjectIds)
    {
        if (!string.Equals(
                candidate.SourceType,
                "Project",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (candidate.ProjectId is null)
        {
            return true;
        }

        var hasActionableTask =
            actionableProjectIds.Contains(
                candidate.ProjectId.Value);

        if (!hasActionableTask)
        {
            return true;
        }

        return IsStrategicallyImportantProject(
            candidate);
    }

    private static bool IsStrategicallyImportantProject(
        MissionCandidate candidate)
    {
        var highBusinessValue =
            candidate.BusinessValue >=
            50000m;

        var dueSoon =
            candidate.DueAt is not null
            &&
            candidate.DueAt.Value <=
            DateTime.Now.AddDays(3);

        return highBusinessValue
               ||
               dueSoon
               ||
               candidate.AtRisk;
    }

    private static string BuildFallbackReason(
        MissionCandidate candidate)
    {
        if (string.Equals(
                candidate.SourceType,
                "Project",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                "This project remains visible because it carries " +
                "strategic value, an approaching deadline, or elevated risk.";
        }

        return
            $"Guardian ranked this {candidate.SourceType.ToLowerInvariant()} " +
            "using its priority, timing, value, risk and estimated effort.";
    }
}