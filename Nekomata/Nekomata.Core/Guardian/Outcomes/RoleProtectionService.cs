using System.Text.RegularExpressions;
using Nekomata.Core.Analytics;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.People;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed record RoleProtectionAdvice(
    bool Applies, string Decision, string Headline, string Explanation,
    int ConfidencePercent, string SuggestedDelegate = "");

public sealed partial class RoleProtectionService(
    IGuardianInterruptionRepository interruptions,
    ITeamProfileRepository teamProfiles,
    RoleAlignmentEngine roleAlignment,
    WeeklyOvertimeService overtime)
{
    public async Task<RoleProtectionAdvice> EvaluateAsync(string description, Mission mission, int remainingMinutes)
    {
        var recordsTask = interruptions.GetAllAsync();
        var profilesTask = teamProfiles.GetAllAsync();
        var alignmentTask = roleAlignment.BuildCurrentWeekSnapshotAsync(DateTime.Now);
        var overtimeTask = overtime.GetCurrentWeekAsync(DateTime.Now);
        await Task.WhenAll(recordsTask, profilesTask, alignmentTask, overtimeTask);
        var recent = recordsTask.Result.Where(item => item.InterruptedAt >= DateTimeOffset.Now.AddDays(-42)).ToList();
        var repeatCount = recent.Count(item => Similar(item.Description, description));
        var delegateMatch = FindDelegate(description, mission, profilesTask.Result);
        return Evaluate(description, mission, remainingMinutes, repeatCount, delegateMatch.Name,
            delegateMatch.Confidence, alignmentTask.Result, overtimeTask.Result.TotalMinutes);
    }

    public static RoleProtectionAdvice Evaluate(
        string description, Mission mission, int remainingMinutes, int priorOccurrences,
        string suggestedDelegate, int delegateConfidence, RoleAlignmentSummary alignment, int overtimeMinutes)
    {
        var text = description.ToLowerInvariant();
        if (ContainsAny(text, "urgent", "outage", "breach", "security incident", "p1", "blocked", "cannot work", "can't work"))
            return new(false, "", "", "", 0);

        var estimatedMinutes = ParseMinutes(text) ?? (ContainsAny(text, "quick", "just", "figures", "report", "reset", "access") ? 45 : 30);
        var routine = ContainsAny(text, "just", "pull", "figures", "report", "reset", "access", "update", "routine", "repeat", "again");
        var strategicMission = mission.BusinessValue >= 10_000 || mission.Score >= 70 ||
                               ContainsAny($"{mission.Title} {mission.Description}".ToLowerInvariant(),
                                   "strategy", "strategic", "transformation", "automation", "project", "design", "implementation");
        var rolePressure = alignment.Bands.Any(band => band.Key is "reactive" or "admin" && band.Status == "Above target");
        var repeated = priorOccurrences >= 2;
        var capacityCost = remainingMinutes > 0 && estimatedMinutes >= Math.Max(20, remainingMinutes / 3);
        var evidenceCount = new[] { routine, repeated, rolePressure, overtimeMinutes >= 30, capacityCost, strategicMission }.Count(value => value);

        if (!strategicMission || evidenceCount < 3)
            return new(false, "", "", "", 0);

        var evidence = new List<string> { $"Expected effort is approximately {estimatedMinutes} minutes." };
        if (repeated) evidence.Add($"Similar requests have already interrupted work {priorOccurrences} time(s) in the last six weeks.");
        if (rolePressure) evidence.Add("Reactive or administrative work is already above its target band for your role.");
        if (overtimeMinutes >= 30) evidence.Add($"Recorded additional time this week is {WeeklyOvertimeSummary.FormatMinutes(overtimeMinutes)}.");
        evidence.Add($"Taking it now would displace protected work on {mission.Title}; approximately {remainingMinutes} minutes remain.");

        if (!string.IsNullOrWhiteSpace(suggestedDelegate) && delegateConfidence >= 45 && (routine || repeated))
        {
            evidence.Add($"{suggestedDelegate} matches the request at {delegateConfidence}% confidence from recorded skills, responsibilities and availability.");
            return new(true, "Delegated", "PROTECT MY ROLE · DELEGATE",
                "Recommendation: delegate. " + string.Join(" ", evidence) + $" Prepare a handoff to {suggestedDelegate}? Nothing will be assigned until you approve.",
                Math.Clamp(62 + evidenceCount * 5 + Math.Min(10, delegateConfidence / 10), 0, 94), suggestedDelegate);
        }

        return new(true, "Queued", "PROTECT MY ROLE · QUEUE",
            "Recommendation: do not switch now. " + string.Join(" ", evidence) +
            " No sufficiently evidenced delegate was found, so keep it behind the current objective and review ownership at the next natural break.",
            Math.Clamp(58 + evidenceCount * 5, 0, 90));
    }

    private static (string Name, int Confidence) FindDelegate(string description, Mission mission, IReadOnlyList<TeamMemberProfile> profiles)
    {
        var text = description.ToLowerInvariant();
        var candidates = profiles.Where(person => person.Active && person.CapacityPercent >= 25 &&
                                                   !person.AvailabilityStatus.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) &&
                                                   (string.IsNullOrWhiteSpace(mission.CurrentOwner) ||
                                                    !person.Name.Equals(mission.CurrentOwner, StringComparison.OrdinalIgnoreCase)))
            .Select(person => (Person: person, Score: DelegateScore(person, text)))
            .OrderByDescending(result => result.Score).ThenByDescending(result => result.Person.CapacityPercent).ToList();
        var best = candidates.FirstOrDefault();
        return best.Person is null || best.Score < 20 ? ("", 0) : (best.Person.Name, Math.Clamp(35 + best.Score, 0, 88));
    }

    private static int DelegateScore(TeamMemberProfile person, string text)
    {
        var score = 0;
        foreach (var skill in person.Skills)
        {
            var terms = new[] { skill.Name }.Concat(SplitTerms(skill.Aliases));
            if (terms.Any(term => term.Length >= 3 && text.Contains(term.ToLowerInvariant())))
                score += skill.Proficiency * 8 + (skill.IsPrimary ? 10 : 0);
        }
        score += TokenOverlap(text, $"{person.Responsibilities} {person.Role}".ToLowerInvariant()) * 4;
        score += person.CapacityPercent >= 75 ? 5 : 0;
        return score;
    }

    private static bool Similar(string left, string right)
    {
        var leftTokens = Tokens(left); var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;
        return leftTokens.Intersect(rightTokens).Count() >= Math.Min(3, Math.Min(leftTokens.Count, rightTokens.Count)) ||
               (double)leftTokens.Intersect(rightTokens).Count() / leftTokens.Union(rightTokens).Count() >= .4;
    }

    private static int TokenOverlap(string left, string right) => Tokens(left).Intersect(Tokens(right)).Count();
    private static HashSet<string> Tokens(string value) => WordRegex().Matches(value.ToLowerInvariant())
        .Select(match => match.Value).Where(word => word.Length >= 4 && !StopWords.Contains(word)).ToHashSet();
    private static IEnumerable<string> SplitTerms(string value) => value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static int? ParseMinutes(string text)
    {
        var match = MinutesRegex().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var minutes)) return minutes;
        var hours = HoursRegex().Match(text);
        return hours.Success && double.TryParse(hours.Groups[1].Value, out var value) ? (int)Math.Round(value * 60) : null;
    }

    private static readonly HashSet<string> StopWords = ["this", "that", "with", "from", "have", "need", "please", "could", "would", "about", "your"];
    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
    [GeneratedRegex(@"\b(\d{1,3})\s*(?:minutes?|mins?)\b", RegexOptions.IgnoreCase)] private static partial Regex MinutesRegex();
    [GeneratedRegex(@"\b(\d+(?:\.\d+)?)\s*(?:hours?|hrs?)\b", RegexOptions.IgnoreCase)] private static partial Regex HoursRegex();
    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase)] private static partial Regex WordRegex();
}
