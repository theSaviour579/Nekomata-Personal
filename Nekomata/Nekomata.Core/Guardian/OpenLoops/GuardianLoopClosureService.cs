using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed record GuardianLoopClosureResult(bool Success, bool NeedsSelection, string Message);

public sealed class GuardianLoopClosureService(
    IGuardianChaseRepository chases,
    IGuardianOpenLoopRepository loops,
    WorkdayJournalService journal)
{
    public static bool IsClosureCommand(string message)
    {
        var value = Normalise(message);
        var removesDraft = value.Contains("remove draft") || value.Contains("cancel draft") ||
                           value.Contains("remove follow up") || value.Contains("cancel follow up");
        return removesDraft && (value.Contains("close loop") || value.Contains("resolve loop") || value.Contains("mark loop closed"));
    }

    public static string ExtractTarget(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message,
            @"(?:draft|follow[- ]?up)(?:\s+(?:for|to))?\s+(.+?)\s+and\s+(?:close|resolve|mark)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    public async Task<GuardianLoopClosureResult> CancelDraftAndResolveAsync(string query, DateTimeOffset now)
    {
        var candidates = new List<(GuardianOpenLoop Loop, GuardianChaseAttempt Attempt)>();
        foreach (var loop in await loops.GetOpenAsync())
        {
            var attempt = (await chases.GetAttemptsAsync(loop.Id)).LastOrDefault(x =>
                x.Status is GuardianChaseStatus.Prepared or GuardianChaseStatus.Approved);
            if (attempt is null) continue;
            if (Matches(query, loop, attempt)) candidates.Add((loop, attempt));
        }

        if (candidates.Count == 0)
            return new(false, false, $"No open loop with an unsent draft matched ‘{query}’. Sent chases cannot be removed from history.");
        if (candidates.Count > 1)
            return new(false, true, "More than one unsent loop matches. Choose one:\n" +
                string.Join("\n", candidates.Select(x => $"• {x.Attempt.Recipient} — {x.Attempt.ContextLabel}")));

        var selected = candidates[0];
        if (!await chases.CancelAsync(selected.Attempt.Id, now))
            return new(false, false, "The draft changed before Guardian could cancel it. Refresh and try again.");
        selected.Attempt.Status = GuardianChaseStatus.Cancelled;

        var evidence = new GuardianLoopEvidence
        {
            EventId = Guid.NewGuid(), OpenLoopId = selected.Loop.Id, OccurredAt = now,
            Kind = "UserDecision", Summary = "User explicitly cancelled the unsent chase draft and closed the dependency."
        };
        if (!await loops.ResolveAsync(selected.Loop.Id, evidence))
            return new(false, false, "The draft was cancelled, but the loop could not be closed because its state changed. Guardian retained the provenance for review.");

        await journal.RecordAsync(new WorkdayEvent
        {
            EventId = evidence.EventId, CorrelationId = selected.Loop.Id, ParentEventId = selected.Attempt.ProvenanceEventId,
            OccurredAt = now, EventType = "OpenLoopDraftCancelledAndResolved", Category = "OpenLoop",
            Title = selected.Loop.Title, Narrative = evidence.Summary, ExternalId = selected.Attempt.Id.ToString("N"),
            Outcome = "Resolved", UserDecision = "Cancelled draft and closed loop", Significance = 65
        });
        return new(true, false, $"Cancelled the unsent follow-up to {selected.Attempt.Recipient} and marked ‘{selected.Loop.Title}’ resolved. Chronicle history was retained.");
    }

    private static bool Matches(string query, GuardianOpenLoop loop, GuardianChaseAttempt attempt)
    {
        var value = Normalise(query);
        if (string.IsNullOrWhiteSpace(value)) return true;
        return new[] { loop.WaitingOn, loop.Title, loop.Detail, attempt.Recipient, attempt.Subject, attempt.ContextLabel }
            .Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalise(string value) => value.Trim().Trim('?', '.', '!').ToLowerInvariant().Replace('’', '\'');
}
