using System.Text;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian;

/// <summary>Everything the scorecard reads. Events come from the Chronicle (workday events) Guardian already keeps.</summary>
public sealed record ScorecardInputs(
    IReadOnlyList<WorkdayEvent> Events,
    int AnswersUp,
    int AnswersDown,
    IReadOnlyList<string> Corrections);

/// <summary>
/// How Guardian's judgement held up: whether its Needs-you items were used or dismissed (and which kinds get dismissed
/// most), what it missed that the user had to catch, what they corrected, whether interruptions were worth it, and
/// whether confirmed actions worked. Built from
/// records Guardian already keeps; no new dashboard. Pure, so it's tested.
/// </summary>
public static class GuardianScorecard
{
    public static string Build(ScorecardInputs i, DateTimeOffset from, DateTimeOffset to)
    {
        var events = i.Events.Where(e => e.OccurredAt >= from && e.OccurredAt <= to).ToList();
        IEnumerable<WorkdayEvent> Of(string type) => events.Where(e => e.EventType.Equals(type, StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder($"HOW GUARDIAN DID · {from.LocalDateTime:d MMM} to {to.LocalDateTime:d MMM}\n");

        // Needs you.
        var raised = Of("AttentionRequired").ToList();
        var opened = Of("AttentionOpened").ToList();
        var dismissed = Of("AttentionResolved").Where(e => e.Outcome.Equals("Dismissed", StringComparison.OrdinalIgnoreCase)).ToList();
        var missed = Of("GuardianMissed").ToList();
        var usefulShare = raised.Count == 0 ? (int?)null : (int)Math.Round(100.0 * Math.Min(raised.Count, opened.Count) / raised.Count);
        var dismissShare = raised.Count == 0 ? (int?)null : (int)Math.Round(100.0 * Math.Min(raised.Count, dismissed.Count) / raised.Count);
        sb.AppendLine().AppendLine(
            raised.Count == 0 ? "Not enough evidence yet: nothing went to Needs you in this period." :
            $"{usefulShare}% of the {raised.Count} Needs-you items were opened; {dismissShare}% were dismissed as not useful; {missed.Count} thing{(missed.Count == 1 ? "" : "s")} you had to catch yourself.");

        sb.AppendLine().AppendLine("NEEDS YOU");
        if (raised.Count == 0) sb.AppendLine("• Nothing raised.");
        else
        {
            sb.AppendLine($"• Raised {raised.Count} · opened {opened.Count} · dismissed as not useful {dismissed.Count}");
            var noisy = raised.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Source: g.Key, Raised: g.Count(), Dismissed: dismissed.Count(d => d.Category.Equals(g.Key, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.Raised >= 3 && x.Dismissed * 2 >= x.Raised).OrderByDescending(x => (double)x.Dismissed / x.Raised).Take(3).ToList();
            foreach (var n in noisy) sb.AppendLine($"• Mostly noise: {n.Source} ({n.Dismissed} of {n.Raised} dismissed)");
        }

        sb.AppendLine().AppendLine("MISSED");
        if (missed.Count == 0) sb.AppendLine("• Nothing reported missed. (Say \"you should have caught that: …\" when I miss something.)");
        foreach (var m in missed.OrderByDescending(m => m.OccurredAt).Take(6)) sb.AppendLine($"• {m.OccurredAt.LocalDateTime:ddd d MMM}: {m.Narrative}");

        sb.AppendLine().AppendLine("ANSWERS AND CORRECTIONS");
        sb.AppendLine(i.AnswersUp + i.AnswersDown == 0 ? "• No answers rated." : $"• Answers rated 👍 {i.AnswersUp} · 👎 {i.AnswersDown}");
        foreach (var c in i.Corrections.Take(5)) sb.AppendLine($"• Corrected: {c}");

        var less = Of("InterruptionFeedback").Count(e => e.Outcome.Equals("less", StringComparison.OrdinalIgnoreCase));
        var more = Of("InterruptionFeedback").Count(e => e.Outcome.Equals("more", StringComparison.OrdinalIgnoreCase));
        var held = Of("InterruptionSuppressed").Count();
        sb.AppendLine().AppendLine("INTERRUPTIONS");
        sb.AppendLine($"• {held} held back during focus · \"that wasn't important\" {less} · \"that was important\" {more}");

        // Judgement calls: pushing back on the user taking work on, and "anything to worry about?" answers.
        var challenges = Of("GuardianChallenge").Count();
        var taken = Of("ChallengeResponse").Count(e => e.Outcome.Equals("Accepted", StringComparison.OrdinalIgnoreCase));
        var kept = Of("ChallengeResponse").Count(e => e.Outcome.Equals("Overridden", StringComparison.OrdinalIgnoreCase));
        var allClears = Of("AllClearAsked").Where(e => e.Outcome.Equals("AllClear", StringComparison.OrdinalIgnoreCase)).ToList();
        var wrongClears = allClears.Count(c => missed.Any(m => m.OccurredAt >= c.OccurredAt && m.OccurredAt <= c.OccurredAt.AddHours(8)));
        if (challenges + allClears.Count > 0)
        {
            sb.AppendLine().AppendLine("JUDGEMENT CALLS");
            if (challenges > 0) sb.AppendLine($"• Pushed back {challenges} time{(challenges == 1 ? "" : "s")} when you took work on · you took the advice {taken}, kept it {kept}");
            if (allClears.Count > 0)
                sb.AppendLine($"• Said nothing needed you {allClears.Count} time{(allClears.Count == 1 ? "" : "s")}" +
                              (wrongClears > 0 ? $" · {wrongClears} of those, you caught something within hours" : " · nothing reported missed after any of them"));
        }

        var actions = Of("ActionConfirmed").ToList();
        var failed = actions.Where(a => a.Outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase)).ToList();
        sb.AppendLine().AppendLine("ACTIONS YOU CONFIRMED");
        sb.AppendLine(actions.Count == 0 ? "• None." : $"• {actions.Count} confirmed · {failed.Count} failed");
        foreach (var f in failed.Take(4)) sb.AppendLine($"• Failed: {f.Narrative}");

        return sb.ToString().TrimEnd();
    }
}
