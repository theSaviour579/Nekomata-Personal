using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Situation;

public sealed class GuardianSituationEngine
{
    public GuardianSituationSnapshot Build(GuardianSituationInput input)
    {
        var signals = new List<GuardianSituationSignal>();
        Add(signals, input.JudgementCount > 0, "Attention", $"{input.JudgementCount} decision{Plural(input.JudgementCount)} require judgement.", "Only exceptions and decisions are counted.", Math.Min(95, 55 + input.JudgementCount * 6));
        Add(signals, input.PriorityEmailCount > 0, "Email", $"{input.PriorityEmailCount} priority message{Plural(input.PriorityEmailCount)} unread.", "Unread messages currently meet the priority threshold.", 55);
        Add(signals, input.OverdueTaskCount > 0, "Tasks", $"{input.OverdueTaskCount} overdue task{Plural(input.OverdueTaskCount)}.", "Tasks are past their due date and need action or a new date.", 65);
        Add(signals, input.AtRiskProjectCount > 0, "Projects", $"{input.AtRiskProjectCount} project{Plural(input.AtRiskProjectCount)} at risk.", "Current project evidence indicates delivery risk.", 72);
        Add(signals, input.QueuedInterruptionCount > 0, "Interruptions", $"{input.QueuedInterruptionCount} signal{Plural(input.QueuedInterruptionCount)} waiting for a natural break.", "Guardian deferred these signals to protect the current commitment.", 42);
        Add(signals, input.BriefingSignalCount > 0, "Briefing", $"{input.BriefingSignalCount} low-urgency signal{Plural(input.BriefingSignalCount)} retained for context.", "These signals remain available without entering the judgement queue.", 28);
        Add(signals, input.OverCapacityMinutes > 0, "Calendar", $"Plan exceeds capacity by {input.OverCapacityMinutes} minutes.", "Calendar commitments and planned work exceed available capacity.", 78);
        Add(signals, input.SystemNeedsAttention, "System", input.SystemHealth, "Guardian may be missing signals until this is fixed.", 85);

        var penalty = Math.Min(32, input.JudgementCount * 7) + Math.Min(15, input.PriorityEmailCount * 3) +
                      Math.Min(18, input.OverdueTaskCount * 4) + Math.Min(20, input.AtRiskProjectCount * 7) +
                      Math.Min(20, input.OverCapacityMinutes / 10) + (input.SystemNeedsAttention ? 15 : 0) +
                      Math.Min(8, input.QueuedInterruptionCount * 2) + Math.Min(4, input.BriefingSignalCount) +
                      (input.CalendarLoaded ? 0 : 12);
        return new()
        {
            ObservedAt = input.Now, CurrentObjective = input.CurrentObjective,
            NowSummary = string.IsNullOrWhiteSpace(input.NowSummary) ? input.CurrentObjective : input.NowSummary,
            NextSummary = string.IsNullOrWhiteSpace(input.NextSummary) ? input.NextDisruption : input.NextSummary,
            LaterSummary = string.IsNullOrWhiteSpace(input.LaterSummary) ?
                (input.JudgementCount == 0 ? "No material pressure identified" : $"{input.JudgementCount} require judgement") : input.LaterSummary,
            JudgementCount = input.JudgementCount, DayConfidence = Math.Clamp(100 - penalty, 5, 100),
            NextDisruption = input.NextDisruption, NextDisruptionAt = input.NextDisruptionAt,
            SystemHealth = input.SystemHealth,
            Signals = signals.OrderByDescending(x => x.Significance).ToList()
        };
    }

    private static void Add(List<GuardianSituationSignal> signals, bool condition, string source,
        string title, string evidence, int significance)
    {
        if (condition) signals.Add(new() { Source = source, Title = title, Evidence = evidence, Significance = significance });
    }

    private static string Plural(int count) => count == 1 ? "" : "s";
}
