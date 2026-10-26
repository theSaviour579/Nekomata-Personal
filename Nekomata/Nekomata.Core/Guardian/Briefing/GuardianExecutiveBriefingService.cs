using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Briefing;

public sealed class GuardianExecutiveBriefingService(
    GuardianOpenLoopAgeService openLoopAgeService,
    IWorkdayEventRepository journal)
{
    public async Task<GuardianExecutiveBriefing> BuildAsync(
        NekomataWorkspace workspace,
        IReadOnlyCollection<CalendarEvent> calendarEvents,
        DateTimeOffset now)
    {
        var result = new GuardianExecutiveBriefing { GeneratedAt = now };
        var loops = await openLoopAgeService.EvaluateAsync(now);
        // Keep the caller's calendar date and offset. Using LocalDateTime here makes
        // the result depend on the host machine's timezone (CI runs in UTC).
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var events = await journal.GetBetweenAsync(start, start.AddDays(1));

        BuildDecisions(workspace, loops, events, result);
        BuildHandledActivity(events, result);
        BuildSchedule(workspace, calendarEvents, now, result);
        BuildDependencies(loops, result);
        BuildCompliance(workspace, result);
        BuildPreparedActions(loops, result);
        BuildAnticipation(workspace, now, result);
        BuildCapacity(workspace, result);

        var greeting = now.Hour < 12 ? "Good morning." : now.Hour < 18 ? "Good afternoon." : "Good evening.";
        var sections = new[]
        {
            result.DecisionSummary,
            result.HandledActivitySummary,
            result.ScheduleSummary,
            result.DependencySummary,
            result.ComplianceSummary,
            result.PreparedActionSummary,
            result.AnticipationSummary,
            result.CapacitySummary
        }.Where(x => !string.IsNullOrWhiteSpace(x));
        result.Narrative = $"{greeting} I've reconciled today's workspace.\n\n" + string.Join("\n\n", sections);
        return result;
    }

    private static void BuildDecisions(NekomataWorkspace workspace, IReadOnlyList<GuardianOpenLoopChase> loops,
        IReadOnlyList<WorkdayEvent> events, GuardianExecutiveBriefing result)
    {
        var compliance = workspace.ProcessRequirements.Count(x =>
            x.State is RequirementState.Blocked or RequirementState.EvidenceRequired);
        var actionableAmbiguity = workspace.UnboundContext
            .Where(x => x.Reason.Contains("requires confirmation", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => $"{x.EntityType}:{x.EntityKey}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(5)
            .ToList();
        var ambiguity = actionableAmbiguity.Count;
        var authorityApprovals = events.Where(x =>
                x.Outcome.Equals("ApprovalRequired", StringComparison.OrdinalIgnoreCase) ||
                x.EventType.Contains("ApprovalRequired", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.CorrelationId).Count();
        var approvals = authorityApprovals + loops.Count(x => x.ChaseState == GuardianChaseState.Prepared);
        var escalations = loops.Count(x => x.ChaseState == GuardianChaseState.EscalationDue);
        result.DecisionCount = compliance + ambiguity + approvals + escalations;
        var summary = result.DecisionCount == 0
            ? "I don't currently need a decision from you."
            : $"I need {result.DecisionCount} decision{Plural(result.DecisionCount)} from you: " +
              string.Join("; ", new[]
              {
                  Item(compliance, "compliance exception"), Item(ambiguity, "ambiguous context item"),
                  Item(approvals, "action awaiting approval"), Item(escalations, "dependency escalation")
              }.Where(x => x.Length > 0)) + ".";
        if (actionableAmbiguity.Count > 0)
            summary += " Context requiring selection: " +
                       string.Join("; ", actionableAmbiguity.Select(x => $"{x.Title} ({x.EntityType})")) + ".";
        result.DecisionSummary = summary;
    }

    private static void BuildHandledActivity(IReadOnlyList<WorkdayEvent> events, GuardianExecutiveBriefing result)
    {
        var completed = events.Where(IsCompletedAdministrativeAction)
            .GroupBy(x => x.EventId).Select(x => x.First()).ToList();
        var reconciliation = events.LastOrDefault(x => x.EventType == "GuardianDayReconciled");
        result.HandledActivitySummary = completed.Count > 0
            ? $"Guardian completed {completed.Count} routine administrative action{Plural(completed.Count)} today. No action is required for those changes."
            : reconciliation is not null
                ? "Guardian has reconciled the workspace; no autonomous administrative changes were completed."
                : "No autonomous administrative changes have been recorded today.";
        foreach (var action in completed.Take(10))
            result.Provenance.Add(new("GuardianAction", action.Title, action.EventId));
        if (reconciliation is not null)
            result.Provenance.Add(new("Chronicle", reconciliation.Title, reconciliation.EventId));
    }

    public static bool IsCompletedAdministrativeAction(WorkdayEvent entry) => entry.EventType is
        "GuardianActionAutoExecuted" or "GuardianRoutineActionHandled" or
        "CalendarEffortAttributed" or "CalendarLinkedToWorkThread" or "CalendarWorkThreadConfirmed" or
        "CalendarTaskConfirmed" or "OpenLoopResolved" or "OpenLoopChasePrepared" or
        "ExpectedWorkCreated" or "ProcessRequirementApplied" or "LearnedWorkflowRulePromoted";

    private static void BuildSchedule(NekomataWorkspace workspace, IReadOnlyCollection<CalendarEvent> calendarEvents,
        DateTimeOffset now, GuardianExecutiveBriefing result)
    {
        var remaining = calendarEvents.Where(x => !x.IsAllDay && x.End > now).OrderBy(x => x.Start).ToList();
        var linkedIds = workspace.CalendarThreadContexts
            .Where(x => x.LinkStatus == WorkThreadLinkStatus.Confirmed)
            .Select(x => x.CalendarEventId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linked = remaining.Count(x => linkedIds.Contains(x.Id));
        var next = remaining.FirstOrDefault();
        result.ScheduleSummary = remaining.Count == 0
            ? "Your remaining calendar is clear."
            : $"You have {remaining.Count} remaining calendar commitment{Plural(remaining.Count)}; {linked} " +
              $"{(linked == 1 ? "is" : "are")} linked to confirmed work. Next is {next!.Subject} at {next.Start:HH:mm}.";
        foreach (var context in workspace.CalendarThreadContexts.Where(x => linkedIds.Contains(x.CalendarEventId)))
            result.Provenance.Add(new("Calendar", context.Explanation, null, context.ThreadId));
    }

    private static void BuildDependencies(IReadOnlyList<GuardianOpenLoopChase> loops, GuardianExecutiveBriefing result)
    {
        var due = loops.Count(x => x.ChaseState is GuardianChaseState.Due or GuardianChaseState.EscalationDue);
        var cooling = loops.Count(x => x.ChaseState == GuardianChaseState.CoolingDown);
        result.DependencySummary = loops.Count == 0
            ? "There are no open external dependencies."
            : $"You are waiting on {loops.Count} thing{Plural(loops.Count)}. {due} need chasing now" +
              (cooling > 0 ? $"; {cooling} {(cooling == 1 ? "is" : "are")} cooling down after a chase" : "") + ".";
        foreach (var loop in loops.Take(5))
            result.Provenance.Add(new("OpenLoop", loop.OpenLoop.Title, loop.OpenLoop.SourceEventId));
    }

    private static void BuildCompliance(NekomataWorkspace workspace, GuardianExecutiveBriefing result)
    {
        var outstanding = workspace.ProcessRequirements.Where(x =>
            x.State is not RequirementState.Satisfied and not RequirementState.Waived and not RequirementState.NotApplicable).ToList();
        var deployment = outstanding.Count(x => x.Gate == ReadinessGate.Deployment);
        var closure = outstanding.Count(x => x.Gate == ReadinessGate.Closure);
        result.ComplianceSummary = outstanding.Count == 0
            ? "No outstanding compliance requirements are currently known."
            : $"{outstanding.Count} compliance requirement{Plural(outstanding.Count)} remain outstanding" +
              (deployment + closure == 0 ? "." : $": {deployment} affect deployment and {closure} affect closure.");
        foreach (var requirement in outstanding.Take(5))
            result.Provenance.Add(new("Requirement", requirement.Name, requirement.Evidence.FirstOrDefault()?.SourceEventId,
                requirement.WorkThreadId));
    }

    private static void BuildPreparedActions(IReadOnlyList<GuardianOpenLoopChase> loops, GuardianExecutiveBriefing result)
    {
        var prepared = loops.Count(x => x.ChaseState == GuardianChaseState.Prepared);
        var approved = loops.Count(x => x.ChaseState == GuardianChaseState.Approved);
        var states = new List<string>();
        if (prepared > 0)
            states.Add($"{prepared} follow-up{Plural(prepared)} {(prepared == 1 ? "is" : "are")} prepared for approval");
        if (approved > 0)
            states.Add($"{approved} {(approved == 1 ? "is" : "are")} approved but not yet confirmed sent");
        result.PreparedActionSummary = states.Count == 0
            ? "No prepared communications are awaiting action."
            : string.Join("; ", states) + ".";
    }

    private static void BuildCapacity(NekomataWorkspace workspace, GuardianExecutiveBriefing result)
    {
        result.CapacitySummary = workspace.Capacity.ExpectedOvertimeMinutes > 0
            ? $"Today's plan exceeds capacity by {workspace.Capacity.ExpectedOvertimeMinutes} minutes; burnout exposure is {workspace.Capacity.BurnoutRisk.ToLowerInvariant()}."
            : $"Approximately {workspace.Capacity.RemainingMinutesToday} minutes of capacity remain; burnout exposure is {workspace.Capacity.BurnoutRisk.ToLowerInvariant()}.";
    }

    private static void BuildAnticipation(NekomataWorkspace workspace, DateTimeOffset now, GuardianExecutiveBriefing result)
    {
        var active = workspace.AnticipatedActions.Where(x => x.ExpiresAt > now &&
            x.Status is not AnticipatedActionStatus.Completed and not AnticipatedActionStatus.Cancelled and not AnticipatedActionStatus.Expired).ToList();
        if (active.Count == 0)
        {
            result.AnticipationSummary = "Guardian has no active anticipatory actions queued.";
            return;
        }
        var waiting = active.Count(x => x.Status == AnticipatedActionStatus.Waiting);
        result.AnticipationSummary = $"Guardian is anticipating {active.Count} next action{Plural(active.Count)}" +
            (waiting > 0 ? $"; {waiting} correctly require no action yet" : "") + ". " +
            string.Join("; ", active.Take(3).Select(x => x.Reason));
        foreach (var action in active.Take(5))
            result.Provenance.Add(new("AnticipatedAction", action.ProposedAction, action.SourceEventId, action.WorkThreadId));
    }

    private static string Item(int count, string label) => count == 0 ? "" : $"{count} {label}{Plural(count)}";
    private static string Plural(int count) => count == 1 ? "" : "s";
}
