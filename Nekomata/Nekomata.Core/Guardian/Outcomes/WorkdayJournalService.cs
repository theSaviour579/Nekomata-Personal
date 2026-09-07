using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Outcomes;

public sealed class WorkdayJournalService(IWorkdayEventRepository repository)
{
    public async Task RecordAsync(WorkdayEvent entry)
    {
        try { await repository.AddAsync(entry); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Workday journal event failed safely: {ex}"); }
    }

    public async Task RecordOncePerDayAsync(WorkdayEvent entry)
    {
        try
        {
            var day = entry.OccurredAt.LocalDateTime.Date;
            var existing = await repository.GetBetweenAsync(new DateTimeOffset(day), new DateTimeOffset(day.AddDays(1)));
            if (existing.Any(x => x.EventType.Equals(entry.EventType, StringComparison.OrdinalIgnoreCase))) return;
            await repository.AddAsync(entry);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Workday boundary event failed safely: {ex}"); }
    }

    public async Task RecordIfAbsentAsync(WorkdayEvent entry)
    {
        try
        {
            if (await repository.ExistsAsync(entry.EventType, entry.ExternalId)) return;
            await repository.AddAsync(entry);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Workday journal idempotent event failed safely: {ex}"); }
    }

    public static IReadOnlyList<GuardianJournalInsight> BuildInsights(IReadOnlyCollection<WorkdayEvent> events)
    {
        var insights = new List<GuardianJournalInsight>();
        var decisions = events.Where(x => x.CorrelationId != Guid.Empty && !string.IsNullOrWhiteSpace(x.UserDecision))
            .GroupBy(x => x.CorrelationId)
            .Where(x => x.Any(y => y.UserDecision.Equals("Accepted", StringComparison.OrdinalIgnoreCase) ||
                                     y.UserDecision.Equals("Rejected", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var accepted = decisions.Where(x => x.Any(y => y.UserDecision.Equals("Accepted", StringComparison.OrdinalIgnoreCase))).ToList();
        var overridden = decisions.Where(x => x.Any(y => y.UserDecision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))).ToList();
        var acceptedSuccess = accepted.Count == 0 ? 0 : accepted.Count(x => x.Any(y => y.Outcome.Equals("Successful", StringComparison.OrdinalIgnoreCase))) * 100d / accepted.Count;
        var overrideSuccess = overridden.Count == 0 ? 0 : overridden.Count(x => x.Any(y => y.Outcome.Equals("Successful", StringComparison.OrdinalIgnoreCase))) * 100d / overridden.Count;
        insights.Add(new GuardianJournalInsight
        {
            Title = "Guardian recommendation calibration",
            Detail = decisions.Count == 0 ? "No explicit recommendation decisions recorded yet." :
                $"{accepted.Count}/{decisions.Count} recommendations accepted. Accepted recommendations currently show {acceptedSuccess:0}% successful outcomes."
        });
        insights.Add(new GuardianJournalInsight
        {
            Title = "Override outcome calibration",
            Detail = overridden.Count == 0 ? "No completed manual-override chains recorded yet." :
                $"{overridden.Count} recommendations overridden. Successful outcomes after override: {overrideSuccess:0}%."
        });
        var deferred = decisions.Where(x => x.Any(y => y.Outcome.Equals("Deferred", StringComparison.OrdinalIgnoreCase))).ToList();
        var deferredSuccess = deferred.Count == 0 ? 0 : deferred.Count(x => x.Any(y => y.Outcome.Equals("Successful", StringComparison.OrdinalIgnoreCase))) * 100d / deferred.Count;
        insights.Add(new GuardianJournalInsight
        {
            Title = "Deferral recommendation calibration",
            Detail = deferred.Count == 0 ? "No completed deferral chains recorded yet." :
                $"{deferred.Count} deferral chain{(deferred.Count == 1 ? "" : "s")}; {deferredSuccess:0}% later reached a successful outcome."
        });
        var value = events.Where(x => x.EventType == "ObjectiveCompleted").Sum(x => x.BusinessValue);
        insights.Add(new GuardianJournalInsight { Title = "Business value delivered", Detail = $"Recorded completed objectives delivered {value:C0} estimated value in this period." });
        return insights;
    }
}
