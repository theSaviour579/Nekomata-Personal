using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Tasks;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class GuardianOutcomeEngine(
    IMissionSessionRepository sessions,
    IGuardianInterruptionRepository interruptions,
    GuardianPolicyAccountabilityService policies) : IGuardianOutcomeEngine
{
    private const int MinimumEvidence = 3;

    public async Task ApplyLearningAsync(NekomataWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var history = await sessions.GetAllAsync();
        var interruptionHistory = await interruptions.GetAllAsync();
        workspace.Outcomes = Analyse(history);
        AddInterruptionInsights(workspace.Outcomes, interruptionHistory);
        workspace.Outcomes.Insights.AddRange(GuardianPatternEngine.Analyse(history, interruptionHistory));
        workspace.Policies = await policies.ReconcileAsync(workspace.Outcomes.Insights);

        foreach (var task in workspace.Tasks)
        {
            var appliedPolicyId = ApplyDurationPrediction(task, history, interruptionHistory);
            if (appliedPolicyId is Guid policyId)
                await policies.RecordDurationCorrectionAppliedAsync(policyId, task.Id, task.ProjectId);
        }

        ApplyProjectStallSignals(workspace, history);
    }

    public static void AddInterruptionInsights(GuardianOutcomeSummary summary, IReadOnlyCollection<GuardianInterruption> history)
    {
        foreach (var group in history.GroupBy(x => (x.InterruptedAt.DayOfWeek, x.InterruptedAt.Hour)))
        {
            var distinctDays = Math.Max(1, group.Select(x => x.InterruptedAt.Date).Distinct().Count());
            if (group.Count() < MinimumEvidence) continue;
            var rate = group.Count() / (double)distinctDays;
            summary.Insights.Add(new GuardianOutcomeInsight
            {
                Kind = "InterruptionWindow", WorkType = group.Key.DayOfWeek.ToString(), SampleSize = group.Count(),
                ConfidencePercent = Math.Min(90, 45 + group.Count() * 5), ObservedRatio = rate,
                PreferredStartHour = group.Key.Hour,
                Summary = $"{group.Key.DayOfWeek} around {group.Key.Hour:00}:00 averages {rate:0.0} interruptions/hour.",
                Explanation = $"Based on {group.Count()} recorded interruptions across {distinctDays} comparable days; avoid fragile deep work here when a stronger window exists."
            });
        }

        foreach (var group in history.Where(x => !string.IsNullOrWhiteSpace(x.WorkType)).GroupBy(x => x.WorkType))
        {
            if (group.Count() < MinimumEvidence) continue;
            var lost = group.Average(x => x.LostMinutes);
            summary.Insights.Add(new GuardianOutcomeInsight
            {
                Kind = "FragmentationCost", WorkType = group.Key, SampleSize = group.Count(),
                ConfidencePercent = Math.Min(90, 45 + group.Count() * 5), ObservedRatio = lost,
                Summary = $"Interrupted {group.Key.ToLowerInvariant()} sessions lose {lost:0} minutes on average.",
                Explanation = "Measured from interrupt capture to triage decision; resume notes preserve context while longer-run duration evidence measures the wider fragmentation penalty."
            });
        }
    }

    public static GuardianOutcomeSummary Analyse(IReadOnlyCollection<MissionSession> history)
    {
        var summary = new GuardianOutcomeSummary { EvidenceCount = history.Count };
        foreach (var group in history.Where(IsUsableCompletion).GroupBy(GetWorkType))
        {
            var ratios = group.Select(x => x.ActualDurationMinutes / (double)x.EstimatedDurationMinutes).ToList();
            var ratio = Median(ratios);
            var confidence = Confidence(group.Count(), ratios);
            summary.Insights.Add(new GuardianOutcomeInsight
            {
                Kind = "DurationAccuracy", WorkType = group.Key, SampleSize = group.Count(),
                ConfidencePercent = confidence, ObservedRatio = ratio,
                Summary = $"{Friendly(group.Key)} work averages {Math.Abs(1 - ratio):P0} {(ratio <= 1 ? "below" : "above")} its original estimate.",
                Explanation = $"Based on {group.Count()} completed comparable missions; median actual/estimate ratio {ratio:P0}.",
                PolicyAction = "apply_duration_correction", IsPolicyActive = confidence >= 75 && group.Count() >= 8,
                AppliedFactor = confidence >= 75 && group.Count() >= 8 ? Math.Clamp(ratio, .35, 2.5) : null
            });
        }

        foreach (var group in history.Where(x => !x.Completed && !x.Cancelled).GroupBy(GetWorkType))
        {
            if (group.Count() < MinimumEvidence) continue;
            summary.Insights.Add(new GuardianOutcomeInsight
            {
                Kind = "RepeatedDeferral", WorkType = group.Key, SampleSize = group.Count(),
                ConfidencePercent = Math.Min(90, 45 + group.Count() * 7),
                Summary = $"{Friendly(group.Key)} work has been deferred {group.Count()} times.",
                Explanation = "Repeated deferral is treated as possible avoidance, weak scoping, or a work item that should be challenged—not merely low priority."
            });
        }

        foreach (var group in history.GroupBy(x => TimeBand(x.StartedAt)))
        {
            if (group.Count() < MinimumEvidence) continue;
            var survival = group.Count(x => x.Completed) / (double)group.Count();
            summary.Insights.Add(new GuardianOutcomeInsight
            {
                Kind = "FocusWindow", WorkType = group.Key, SampleSize = group.Count(),
                ConfidencePercent = Math.Min(90, 40 + group.Count() * 6), ObservedRatio = survival,
                PreferredStartHour = group.Key == "Morning" ? 8 : group.Key == "Afternoon" ? 13 : 17,
                Summary = $"{group.Key} focus blocks survive {survival:P0} of the time.",
                Explanation = $"{group.Count(x => x.Completed)} of {group.Count()} missions completed in this time window."
            });
        }
        return summary;
    }

    private static Guid? ApplyDurationPrediction(
        NekomataTask task,
        IReadOnlyCollection<MissionSession> history,
        IReadOnlyCollection<GuardianInterruption> interruptionHistory)
    {
        Guid? appliedPolicyId = null;
        var workType = GetWorkType(task);
        var comparable = history.Where(IsUsableCompletion).Where(x => GetWorkType(x) == workType).ToList();
        var original = Math.Max(task.EstimatedMinutes, 1);
        task.OriginalEstimatedMinutes = original;
        var predicted = original;
        var correction = 1d;
        IReadOnlyCollection<double> ratios = [];
        if (comparable.Count >= MinimumEvidence)
        {
            ratios = comparable.Select(x => x.ActualDurationMinutes / (double)x.EstimatedDurationMinutes).ToList();
            var observed = Math.Clamp(Median(ratios), 0.35, 2.5);
            var confidence = Confidence(comparable.Count, ratios);
            var isActivePolicy = confidence >= 75 && comparable.Count >= 8;
            var evidenceWeight = isActivePolicy ? 1d : Math.Min(0.60, comparable.Count * 0.075);
            correction = Math.Clamp(1 + (observed - 1) * evidenceWeight, 0.85, 1.15);
            if (isActivePolicy)
            {
                correction = observed;
                appliedPolicyId = GuardianPolicyAccountabilityService.ComputePolicyId("apply_duration_correction", workType);
            }
            predicted = Math.Max(5, RoundToFive(original * correction));
            task.EstimateConfidencePercent = confidence;
        }

        var disruptions = interruptionHistory.Where(x => x.WorkType.Equals(task.Source, StringComparison.OrdinalIgnoreCase) ||
            x.WorkType.Equals(workType, StringComparison.OrdinalIgnoreCase)).ToList();
        var overhead = disruptions.Count >= MinimumEvidence
            ? Math.Max(0, RoundToFive(disruptions.Average(x => x.LostMinutes)))
            : 0;
        task.PredictedActiveMinutes = predicted;
        task.ExpectedInterruptionOverheadMinutes = overhead;
        task.PredictedCalendarMinutes = predicted + overhead;
        task.EstimateLearningExplanation = comparable.Count >= MinimumEvidence
            ? $"{original}m entered → {predicted}m predicted active effort ({correction:P0} gradual personal correction) → +{overhead}m expected interruption overhead → {predicted + overhead}m calendar requirement. Evidence: {comparable.Count} comparable missions; confidence {task.EstimateConfidencePercent}%."
            : $"{original}m entered → {predicted}m predicted active effort → +{overhead}m expected interruption overhead → {predicted + overhead}m calendar requirement. More comparable outcomes are required before personal correction is applied.";
        return appliedPolicyId;
    }

    private static void ApplyProjectStallSignals(NekomataWorkspace workspace, IReadOnlyCollection<MissionSession> history)
    {
        foreach (var project in workspace.Projects)
        {
            var deferred = history.Count(x => x.ProjectId == project.Id && !x.Completed && !x.Cancelled);
            if (deferred < 3) continue;
            project.AtRisk = true;
            if (!string.IsNullOrWhiteSpace(project.NextAction) && !project.NextAction.Contains("stalled", StringComparison.OrdinalIgnoreCase))
                project.NextAction += $" · Guardian: progress stalled across {deferred} deferred missions";
        }
    }

    private static bool IsUsableCompletion(MissionSession x) =>
        x.Completed && x.EstimatedDurationMinutes > 0 && x.ActualDurationMinutes > 0;

    private static string GetWorkType(MissionSession session) => Classify(session.Title, session.SourceType);
    private static string GetWorkType(NekomataTask task) => Classify(task.Title + " " + task.Description + " " + task.Category + " " + task.Tags,
        task.RequiresSql ? "SQL" : task.Source);

    public static string Classify(string? text, string? source)
    {
        var value = $"{text} {source}".ToLowerInvariant();
        if (value.Contains("sql") || value.Contains("analysis") || value.Contains("data") || value.Contains("report")) return "Analysis";
        if (value.Contains("request") || value.Contains("issue") || value.Contains("incident") || value.Contains("chase")) return "Operational";
        if (value.Contains("project") || value.Contains("planning") || value.Contains("strategy")) return "Project";
        if (value.Contains("email") || value.Contains("outlook") || value.Contains("reply")) return "Communication";
        return string.IsNullOrWhiteSpace(source) ? "General" : source.Trim();
    }

    private static string TimeBand(DateTime start) => start.Hour < 12 ? "Morning" : start.Hour < 17 ? "Afternoon" : "Evening";
    private static string Friendly(string value) => value.Equals("Analysis", StringComparison.OrdinalIgnoreCase) ? "Analysis" : value;
    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return 1;
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }

    private static int Confidence(int count, IReadOnlyCollection<double> ratios)
    {
        var mean = ratios.Count == 0 ? 1 : ratios.Average();
        var deviation = ratios.Count == 0 ? 1 : ratios.Average(x => Math.Abs(x - mean));
        return Math.Clamp((int)Math.Round(45 + Math.Min(count, 10) * 5 - deviation * 25), 35, 95);
    }

    private static int RoundToFive(double value) => (int)Math.Round(value / 5d, MidpointRounding.AwayFromZero) * 5;
}
