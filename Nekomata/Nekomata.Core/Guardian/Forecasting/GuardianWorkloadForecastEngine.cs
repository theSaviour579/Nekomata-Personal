using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Forecasting;

public sealed class GuardianWorkloadForecastEngine
{
    public GuardianWorkloadForecast Build(GuardianWorkloadForecastInput input)
    {
        var workable = Math.Max(0, input.RemainingWorkableMinutes);
        var fixedMinutes = Math.Max(0, input.FixedCommitmentMinutes);
        var planned = Math.Max(0, input.PlannedWorkMinutes);
        var interruption = Math.Max(0, input.InterruptionAllowanceMinutes);
        var requiredWork = fixedMinutes + planned + interruption;
        var atRisk = Math.Max(0, requiredWork - workable);
        var elapsedToFinish = requiredWork + Math.Max(0, input.NonWorkingMinutesRemaining);
        var finish = input.Now.AddMinutes(elapsedToFinish);

        var confidence = 45 + (input.CalendarLoaded ? 20 : 0) +
                         Math.Min(20, input.InterruptionEvidenceCount * 2) +
                         (input.WorkItems.Count > 0 || planned == 0 ? 10 : 0);
        var threatened = FindThreatened(input.WorkItems, Math.Max(0, workable - fixedMinutes - interruption));
        var status = !input.CalendarLoaded ? "PROVISIONAL" : atRisk switch
        {
            >= 120 => "CRITICAL",
            >= 60 => "AT RISK",
            > 0 => "WATCH",
            _ => "ON TRACK"
        };
        var summary = atRisk > 0
            ? $"The current plan is likely to run {atRisk} minutes beyond workable capacity. {ThreatSummary(threatened)}"
            : $"The current plan fits with {Math.Max(0, workable - requiredWork)} workable minutes of resilience remaining.";
        if (!input.CalendarLoaded) summary = "Calendar evidence is not loaded; this forecast is provisional. " + summary;
        return new()
        {
            CalculatedAt = input.Now, ProjectedFinishAt = finish,
            RemainingWorkableMinutes = workable, PlannedWorkMinutes = planned,
            FixedCommitmentMinutes = fixedMinutes, InterruptionAllowanceMinutes = interruption,
            MinutesAtRisk = atRisk, ConfidencePercent = Math.Clamp(confidence, 45, 95),
            StatusLabel = status, Summary = summary, ThreatenedCommitments = threatened,
            Evidence = $"{workable} workable min remaining · {fixedMinutes} min fixed commitments · {planned} min planned work · {interruption} min learned interruption allowance."
        };
    }

    private static List<string> FindThreatened(IReadOnlyList<GuardianWorkloadItem> items, int capacity)
    {
        var cumulative = 0;
        var threatened = new List<string>();
        foreach (var item in items.OrderByDescending(x => x.IsCritical).ThenByDescending(x => x.Score).ThenBy(x => x.DueAt))
        {
            cumulative += Math.Max(0, item.Minutes);
            if (cumulative > capacity) threatened.Add(item.Title);
        }
        return threatened.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
    }

    private static string ThreatSummary(IReadOnlyCollection<string> threatened) => threatened.Count == 0
        ? "No individual commitment can be isolated confidently yet."
        : $"Likely to slip: {string.Join(", ", threatened)}.";
}
