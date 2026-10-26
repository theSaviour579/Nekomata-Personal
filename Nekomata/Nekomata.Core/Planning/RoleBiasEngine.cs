using Nekomata.Core.Analytics;

namespace Nekomata.Core.Planning;

public sealed record RoleBias(
    string BandKey, string BandName, string Direction, int Strength,
    int ConsecutiveWeeks, double CurrentDeviationPoints, string MainReason,
    IReadOnlyList<string> Evidence)
{
    public string StrengthLabel => Strength switch { < 30 => "Light", < 60 => "Moderate", < 80 => "Strong", _ => "Very strong" };
}

public sealed record RoleBiasSnapshot(DateTime WeekStarting, string BandKey, string Direction, int Strength, string Reason);

public sealed class RoleBiasEngine
{
    public IReadOnlyList<RoleBias> Calculate(
        RoleAlignmentSummary current,
        IReadOnlyList<RoleAlignmentSummary> weeklyHistory,
        IReadOnlyDictionary<string, double>? causeWeights = null,
        IReadOnlyList<RoleBiasSnapshot>? storedHistory = null,
        DateTime? currentWeekStarting = null)
    {
        if (!current.HasEvidence) return [];
        var result = new List<RoleBias>();
        foreach (var band in current.Bands)
        {
            var direction = band.ActualPercent < band.MinimumPercent ? "Underweight" :
                band.ActualPercent > band.MaximumPercent ? "Overweight" : "Recovered";
            var priorActive = (storedHistory ?? []).Where(item => item.BandKey == band.Key &&
                (currentWeekStarting is null || item.WeekStarting.Date < currentWeekStarting.Value.Date))
                .OrderByDescending(item => item.WeekStarting).FirstOrDefault();
            if (direction == "Recovered")
            {
                if (priorActive is { Strength: > 10 })
                    result.Add(new(band.Key, band.Name, "Decaying", Math.Max(0, priorActive.Strength - 20), 0, 0,
                        "The operating band has recovered; the previous bias is decaying rather than disappearing immediately.",
                        [$"Previous stored strength: {priorActive.Strength}/100.", "Decay applied: 20 points this weekly observation."]));
                continue;
            }

            var consecutive = ConsecutiveDeviationWeeks(band.Key, direction, weeklyHistory, current);
            var deviation = direction == "Underweight" ? band.MinimumPercent - band.ActualPercent : band.ActualPercent - band.MaximumPercent;
            var durationStrength = consecutive switch { <= 1 => 20, 2 => 35, 3 => 50, 4 => 62, 5 => 74, _ => 86 };
            var magnitude = Math.Min(14, (int)Math.Round(deviation * .7));
            var causeWeight = Math.Clamp(causeWeights?.GetValueOrDefault(band.Key, 1) ?? 1, .1, 1);
            var strength = Math.Clamp((int)Math.Round((durationStrength + magnitude) * causeWeight), 8, 100);
            var evidenceWeight = current.HasSufficientEvidence ? 1 : Math.Clamp(current.EvidenceConfidencePercent / 100d, .15, .6);
            strength = Math.Clamp((int)Math.Round(strength * evidenceWeight), 5,
                current.HasSufficientEvidence ? 100 : 29);
            var causeText = causeWeight < .75 ? $" Cause evidence damped correction pressure to {causeWeight:P0}." : "";
            var maturityText = current.HasSufficientEvidence ? "" :
                $" Current-week evidence is {current.EvidenceMaturityLabel.ToLowerInvariant()}, so planning pressure is capped at light strength.";
            var reason = $"{band.Name} is {deviation:0.#} percentage points {direction.ToLowerInvariant()} after {consecutive} consecutive recorded week(s).{causeText}{maturityText}";
            result.Add(new(band.Key, band.Name, direction, strength, consecutive, deviation, reason,
                [$"Observed {band.ActualPercent:0.#}% against target {band.TargetLabel}.",
                 $"Duration component {durationStrength}/100; magnitude component {magnitude}/14; cause weight {causeWeight:0.00}; evidence weight {evidenceWeight:0.00}."]));
        }
        return result.OrderByDescending(item => item.Strength).ThenByDescending(item => item.CurrentDeviationPoints).ToList();
    }

    public static IReadOnlyList<RoleBiasSnapshot> UpdateHistory(
        IReadOnlyList<RoleBiasSnapshot> existing, IEnumerable<RoleBias> current, DateTime weekStarting)
    {
        var result = existing.Where(item => item.WeekStarting.Date != weekStarting.Date).ToList();
        result.AddRange(current.Select(item => new RoleBiasSnapshot(weekStarting.Date, item.BandKey, item.Direction, item.Strength, item.MainReason)));
        return result;
    }

    private static int ConsecutiveDeviationWeeks(
        string key, string direction, IReadOnlyList<RoleAlignmentSummary> history, RoleAlignmentSummary current)
    {
        var values = history.Concat([current]).TakeLast(6).Reverse().ToList();
        var count = 0;
        foreach (var summary in values)
        {
            var band = summary.Bands.FirstOrDefault(item => item.Key == key);
            if (band is null || !summary.HasSufficientEvidence) break;
            var matches = direction == "Underweight" ? band.ActualPercent < band.MinimumPercent : band.ActualPercent > band.MaximumPercent;
            if (!matches) break;
            count++;
        }
        return Math.Max(1, count);
    }
}
