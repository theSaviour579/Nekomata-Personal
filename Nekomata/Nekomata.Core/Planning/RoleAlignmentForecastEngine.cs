using Nekomata.Core.Analytics;

namespace Nekomata.Core.Planning;

public enum ProjectedActivityDisposition { Planned, Proposed, Accepted, Rejected }
public sealed record ProjectedRoleActivity(string BandKey, int Minutes, string Source, ProjectedActivityDisposition Disposition, bool RemovesWork = false);
public sealed record AlignmentForecastPoint(string Label, int Score, bool IsEstimate, IReadOnlyList<RoleAlignmentBand> Bands, string EvidenceLabel, bool IsProvisional = false)
{
    public string ScoreLabel => IsProvisional ? $"{Score}% · PROVISIONAL" : $"{Score}%";
}
public sealed record RoleAlignmentForecast(
    AlignmentForecastPoint Current, AlignmentForecastPoint NoIntervention,
    AlignmentForecastPoint RecommendedPlan, double ExpectedImprovement,
    IReadOnlyList<string> Evidence)
{
    public string ExpectedImprovementLabel => $"Estimated +{ExpectedImprovement:0.#} points";
}

public sealed class RoleAlignmentForecastEngine
{
    public RoleAlignmentForecast Calculate(
        RoleAlignmentSummary current,
        IReadOnlyCollection<ProjectedRoleActivity> scheduledAndPlanned,
        IReadOnlyCollection<ProjectedRoleActivity> correctiveActions,
        RoleAlignmentSettings? settings = null)
    {
        settings ??= new();
        var observed = current.Bands.ToDictionary(band => band.Key, band => band.Minutes);
        var noInterventionActivities = scheduledAndPlanned.Where(item => item.Disposition is ProjectedActivityDisposition.Planned or ProjectedActivityDisposition.Accepted).ToList();
        var acceptedCorrections = correctiveActions.Where(item => item.Disposition == ProjectedActivityDisposition.Accepted).ToList();
        var recommendedCorrections = correctiveActions.Where(item => item.Disposition is ProjectedActivityDisposition.Accepted or ProjectedActivityDisposition.Proposed).ToList();
        var noIntervention = Project("No intervention · ESTIMATE", observed, noInterventionActivities.Concat(acceptedCorrections), settings, current.IsProvisional);
        var recommended = Project("Recommended plan · ESTIMATE", observed, noInterventionActivities.Concat(recommendedCorrections), settings, current.IsProvisional);
        return new(
            new("Current · OBSERVED", current.Score, false, current.Bands, current.EvidenceMaturityDetail, current.IsProvisional),
            noIntervention, recommended, Math.Round((double)(recommended.Score - noIntervention.Score), 1),
            [$"Observed base: {current.RecordedMinutes} minutes.",
             $"No-intervention inputs: {noInterventionActivities.Count + acceptedCorrections.Count} scheduled, planned or approved item(s).",
             $"Recommended-plan inputs: {recommendedCorrections.Count} proposed or approved correction(s); rejected actions are excluded."]);
    }

    private static AlignmentForecastPoint Project(
        string label, IReadOnlyDictionary<string, int> observed,
        IEnumerable<ProjectedRoleActivity> activities, RoleAlignmentSettings settings, bool provisional)
    {
        var minutes = observed.ToDictionary(item => item.Key, item => item.Value);
        var sources = new List<string>();
        foreach (var activity in activities)
        {
            var delta = activity.RemovesWork ? -activity.Minutes : activity.Minutes;
            minutes[activity.BandKey] = Math.Max(0, minutes.GetValueOrDefault(activity.BandKey) + delta);
            sources.Add(activity.Source);
        }
        var total = minutes.Values.Sum();
        var bands = settings.Targets.Select(target =>
        {
            var actualMinutes = minutes.GetValueOrDefault(target.Key);
            var actual = total == 0 ? 0 : Math.Round(actualMinutes * 100d / total, 1);
            var status = actual < target.MinimumPercent ? "Below target" : actual > target.MaximumPercent ? "Above target" : "Within range";
            return new RoleAlignmentBand(target.Key, target.Name, actualMinutes, actual,
                target.MinimumPercent, target.MaximumPercent, status, "Projected from observed and planned minutes.");
        }).ToList();
        var deviation = bands.Sum(band => band.ActualPercent < band.MinimumPercent
            ? band.MinimumPercent - band.ActualPercent : band.ActualPercent > band.MaximumPercent ? band.ActualPercent - band.MaximumPercent : 0);
        var score = total == 0 ? 0 : Math.Clamp((int)Math.Round(100 - deviation), 0, 100);
        var evidence = sources.Count == 0 ? "No future activity was available" : $"{sources.Count} future input(s); deterministic estimate";
        if (provisional) evidence += " · based on provisional observed evidence";
        return new(label, score, true, bands, evidence, provisional);
    }
}
