using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;

namespace Nekomata.Core.Guardian.Outcomes;

public static class GuardianPatternEngine
{
    private const int PolicyEvidence = 8;
    private const int PolicyConfidence = 75;

    public static IReadOnlyList<GuardianOutcomeInsight> Analyse(
        IReadOnlyCollection<MissionSession> sessions, IReadOnlyCollection<GuardianInterruption> interruptions)
    {
        var insights = new List<GuardianOutcomeInsight>();
        AddInterruptibility(insights, sessions, interruptions, "Morning", 8, 11);
        AddInterruptibility(insights, sessions, interruptions, "Afternoon", 13, 16);
        AddAnalysisCompletion(insights, sessions);
        AddCapacityDamage(insights, sessions, interruptions);
        return insights;
    }

    private static void AddInterruptibility(List<GuardianOutcomeInsight> output,
        IReadOnlyCollection<MissionSession> sessions, IReadOnlyCollection<GuardianInterruption> interruptions,
        string band, int fromHour, int toHour)
    {
        var comparable = sessions.Where(x => x.StartedAt.Hour >= fromHour && x.StartedAt.Hour < toHour).ToList();
        if (comparable.Count < 3) return;
        var interrupted = comparable.Count(session => interruptions.Any(item =>
            item.InterruptedAt.LocalDateTime >= session.StartedAt &&
            item.InterruptedAt.LocalDateTime <= (session.FinishedAt > session.StartedAt
                ? session.FinishedAt : session.StartedAt.AddMinutes(Math.Max(15, session.EstimatedDurationMinutes)))));
        var rate = interrupted / (double)comparable.Count;
        var confidence = Confidence(comparable.Count, rate);
        var policy = comparable.Count >= PolicyEvidence && confidence >= PolicyConfidence;
        output.Add(new GuardianOutcomeInsight
        {
            Kind = "InterruptibilityPattern", WorkType = band, SampleSize = comparable.Count,
            ConfidencePercent = confidence, ObservedRatio = rate, PreferredStartHour = fromHour,
            Summary = $"{fromHour:00}:00–{toHour:00}:00 focus blocks are interrupted {rate:P0} of the time.",
            Explanation = $"{interrupted} of {comparable.Count} comparable focus sessions overlapped a recorded interruption.",
            PolicyAction = band == "Afternoon" ? "avoid_deep_afternoon" : "prefer_deep_morning",
            IsPolicyActive = policy
        });
    }

    private static void AddAnalysisCompletion(List<GuardianOutcomeInsight> output, IReadOnlyCollection<MissionSession> sessions)
    {
        var comparable = sessions.Where(x => GuardianOutcomeEngine.Classify(x.Title, x.SourceType) == "Analysis" && x.StartedAt.Hour < 11).ToList();
        if (comparable.Count < 3) return;
        var rate = comparable.Count(x => x.Completed) / (double)comparable.Count;
        var confidence = Confidence(comparable.Count, rate);
        output.Add(new GuardianOutcomeInsight
        {
            Kind = "CompletionPattern", WorkType = "Analysis", SampleSize = comparable.Count,
            ConfidencePercent = confidence, ObservedRatio = rate, PreferredStartHour = 8,
            Summary = $"Analysis work started before 11:00 completes {rate:P0} of the time.",
            Explanation = $"Based on {comparable.Count} comparable analysis missions.",
            PolicyAction = "prefer_analysis_before_11",
            IsPolicyActive = comparable.Count >= PolicyEvidence && confidence >= PolicyConfidence && rate >= .70
        });
    }

    private static void AddCapacityDamage(List<GuardianOutcomeInsight> output,
        IReadOnlyCollection<MissionSession> sessions, IReadOnlyCollection<GuardianInterruption> interruptions)
    {
        var overtimeDays = sessions.Where(x => x.FinishedAt.Hour >= 17).Select(x => x.StartedAt.Date).Distinct().ToList();
        if (overtimeDays.Count < 3) return;
        var damaged = overtimeDays.Count(day => interruptions.Any(x => x.InterruptedAt.LocalDateTime.Date == day));
        var ratio = damaged / (double)overtimeDays.Count;
        var confidence = Confidence(overtimeDays.Count, ratio);
        var afternoon = interruptions.Where(x => x.InterruptedAt.Hour >= 12)
            .GroupBy(x => x.InterruptedAt.DayOfWeek).OrderByDescending(x => x.Count()).FirstOrDefault();
        var weeklyRecovery = interruptions.Where(x => x.InterruptedAt.Hour >= 12).Sum(x => x.LostMinutes) /
                             Math.Max(1d, interruptions.Select(x => ISOWeek(x.InterruptedAt.LocalDateTime)).Distinct().Count());
        output.Add(new GuardianOutcomeInsight
        {
            Kind = "CapacityDamagePattern", WorkType = "Interruptions", SampleSize = overtimeDays.Count,
            ConfidencePercent = confidence, ObservedRatio = ratio,
            Summary = $"Interruptions occur on {ratio:P0} of days that finish beyond normal capacity.",
            Explanation = $"{afternoon?.Key.ToString() ?? "Afternoons"} is the highest observed afternoon risk. Protecting interrupted capacity could recover approximately {weeklyRecovery / 60d:0.0} hours/week.",
            PolicyAction = "protect_morning_strategic_block",
            IsPolicyActive = overtimeDays.Count >= PolicyEvidence && confidence >= PolicyConfidence && ratio >= .60
        });
    }

    private static int Confidence(int samples, double proportion)
    {
        var stability = 1 - Math.Min(.5, Math.Abs(.5 - proportion));
        return Math.Clamp((int)Math.Round(40 + Math.Min(samples, 20) * 2.5 + stability * 10), 40, 95);
    }

    private static string ISOWeek(DateTime value) => $"{System.Globalization.ISOWeek.GetYear(value)}-{System.Globalization.ISOWeek.GetWeekOfYear(value)}";
}
