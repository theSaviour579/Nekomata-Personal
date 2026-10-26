using System.Security.Cryptography;
using System.Text;
using Nekomata.Core.Guardian.Authority;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed class AnticipatoryOperationsEngine(
    GuardianActionAuthorityEngine authority,
    GuardianOpenLoopAgeService openLoops,
    OperationalIntentDetectionService intents,
    WorkdayJournalService journal)
{
    public async Task<IReadOnlyList<AnticipatedAction>> EvaluateAsync(
        NekomataWorkspace workspace, IReadOnlyCollection<CalendarEvent> calendar, DateTimeOffset now)
    {
        var previous = workspace.AnticipatedActions.ToDictionary(x => x.Id);
        var queue = new List<AnticipatedAction>();
        var loopStates = await openLoops.EvaluateAsync(now);
        workspace.WorkThreadNextActions = new WorkThreadNextActionEngine().Derive(workspace, loopStates, now).ToList();
        AddMeetingPreparation(workspace, calendar, now, queue);
        AddReadinessChecks(workspace, calendar, now, queue);
        await AddDetectedIntentsAsync(workspace, calendar, now, queue);
        AddThreadNextActions(workspace, now, queue);

        foreach (var item in queue)
        {
            Classify(item);
            if (previous.TryGetValue(item.Id, out var prior) && prior.Status is AnticipatedActionStatus.Approved or
                    AnticipatedActionStatus.Sent or AnticipatedActionStatus.Failed or AnticipatedActionStatus.Completed or
                    AnticipatedActionStatus.Cancelled)
            {
                item.Status = prior.Status;
                item.ApprovedAt = prior.ApprovedAt;
                item.SentAt = prior.SentAt;
                item.LastSendAttemptAt = prior.LastSendAttemptAt;
                item.LastSendError = prior.LastSendError;
            }
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = item.SourceEventId, CorrelationId = item.Id, OccurredAt = now,
                EventType = "AnticipatedActionQueued", Category = "Anticipation", Title = item.ProposedAction,
                Narrative = $"{item.Reason} Authority: {item.Authority}; status: {item.Status}.",
                ExternalId = item.Id.ToString("N"), Outcome = item.Status.ToString(), Significance = 45
            });
            if (item.ProposedAction == "PrepareMeetingContext" && !string.IsNullOrWhiteSpace(item.PreparedContent))
                await journal.RecordIfAbsentAsync(new WorkdayEvent
                {
                    EventId = StableId($"{item.Id:N}:prepared"), CorrelationId = item.Id, OccurredAt = now,
                    EventType = "MeetingContextPrepared", Category = "Anticipation", Title = item.ProposedAction,
                    Narrative = "Guardian deterministically prepared context for a confirmed upcoming meeting.",
                    ExternalId = $"{item.Id:N}:prepared", Outcome = "Prepared", Significance = 50
                });
        }
        workspace.AnticipatedActions = queue.OrderBy(x => x.ExecuteAfter ?? now).ThenByDescending(x => x.Confidence).ToList();
        var priorInterventions = workspace.ScheduledInterventions.ToList();
        workspace.ScheduledInterventions = new ScheduledInterventionEngine()
            .Reconcile(workspace.AnticipatedActions, priorInterventions, now).ToList();
        workspace.PredictionCalibration = new PredictionCalibrationEngine()
            .Evaluate(workspace.ScheduledInterventions, now);
        foreach (var intervention in workspace.ScheduledInterventions)
        {
            var eventType = intervention.Status == ScheduledInterventionStatus.Superseded
                ? "ScheduledInterventionSuperseded" : "ScheduledInterventionReconciled";
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = StableId($"{intervention.Id:N}:{eventType}:{intervention.Status}"),
                CorrelationId = intervention.PredictionId, OccurredAt = now, EventType = eventType,
                Category = "Anticipation", Title = intervention.Title,
                Narrative = $"{intervention.Reason} Trigger: {intervention.TriggerAt:O}; authority: {intervention.Authority}; status: {intervention.Status}.",
                ExternalId = $"{intervention.Id:N}:{intervention.Status}", Outcome = intervention.Status.ToString(), Significance = 45
            });
        }
        foreach (var outcome in workspace.PredictionCalibration.Outcomes
                     .Where(x => x.Disposition != PredictionOutcomeDisposition.Pending))
        {
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = StableId($"{outcome.InterventionId:N}:calibration:{outcome.Disposition}"),
                CorrelationId = outcome.PredictionId, OccurredAt = outcome.ObservedAt ?? now,
                EventType = "PredictionOutcomeMeasured", Category = "Anticipation",
                Title = outcome.Title, Narrative = outcome.Reason,
                ExternalId = $"{outcome.InterventionId:N}:{outcome.Disposition}",
                Outcome = outcome.Disposition.ToString(), Significance = 50
            });
        }
        return workspace.AnticipatedActions;
    }

    private async Task AddDetectedIntentsAsync(NekomataWorkspace workspace, IReadOnlyCollection<CalendarEvent> calendar,
        DateTimeOffset now, ICollection<AnticipatedAction> queue)
    {
        workspace.DetectedOperationalIntents = (await intents.DetectAsync(now)).ToList();
        foreach (var intent in workspace.DetectedOperationalIntents.Where(x => x.Type == OperationalIntentType.ScheduleMeeting))
        {
            var window = ResolveRequestedWindow(intent.RequestedWindow, now);
            var calendarLoaded = calendar.Any(x => x.Start.LocalDateTime.Date == window.Date.LocalDateTime.Date);
            var slot = calendarLoaded ? FindFreeSlot(calendar, window.Start, window.End, intent.DurationMinutes, now) : null;
            var action = slot is null ? "PrepareMeetingReplyAfterCalendarCheck" : "PrepareMeetingReply";
            var reason = $"{intent.Person} asked for {intent.DurationMinutes} minutes ({intent.RequestedWindow}). " +
                         (slot is not null ? $"{slot.Value.Start:HH:mm}–{slot.Value.End:HH:mm} is the earliest verified free slot."
                             : calendarLoaded ? "No suitable free slot exists in the requested window."
                             : "The requested window is not loaded; verify availability before offering a time.");
            var anticipated = Create("TeamsMeetingRequest", intent.SignalId.ToString("N"), intent.WorkThreadId, action, reason,
                [intent.EvidenceSummary, calendarLoaded ? "Requested calendar window loaded" : "Calendar verification required"],
                intent.Confidence, "Prepares a timely response without sending or inventing availability.", now, now, window.End);
            if (slot is not null)
            {
                anticipated.SuggestedStart = slot.Value.Start;
                anticipated.SuggestedEnd = slot.Value.End;
                anticipated.PreparedContent = BuildMeetingReply(intent.Person, intent.DurationMinutes, slot.Value.Start, workspace, intent.WorkThreadId);
                anticipated.TargetConversationId = intent.TargetConversationId;
            }
            queue.Add(anticipated);
        }
    }

    private static (DateTimeOffset Date, DateTimeOffset Start, DateTimeOffset End) ResolveRequestedWindow(string requestedWindow, DateTimeOffset now)
    {
        var date = requestedWindow.StartsWith("Tomorrow", StringComparison.OrdinalIgnoreCase)
            ? now.LocalDateTime.Date.AddDays(1) : now.LocalDateTime.Date;
        var startHour = requestedWindow.Contains("afternoon", StringComparison.OrdinalIgnoreCase) ? 12 : 8;
        var endHour = requestedWindow.Contains("morning", StringComparison.OrdinalIgnoreCase) ? 12 : 17;
        var day = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        return (day, day.AddHours(startHour), day.AddHours(endHour));
    }

    private static (DateTimeOffset Start, DateTimeOffset End)? FindFreeSlot(IEnumerable<CalendarEvent> calendar,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, int durationMinutes, DateTimeOffset now)
    {
        var cursor = windowStart > now.AddMinutes(15) ? windowStart : now.AddMinutes(15);
        if (cursor.Minute is > 0 and <= 30) cursor = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 30, 0, cursor.Offset);
        else if (cursor.Minute > 30) cursor = new DateTimeOffset(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Offset).AddHours(1);
        foreach (var busy in calendar.Where(x => !x.IsAllDay && x.End > windowStart && x.Start < windowEnd).OrderBy(x => x.Start))
        {
            if (cursor.AddMinutes(durationMinutes) <= busy.Start) return (cursor, cursor.AddMinutes(durationMinutes));
            if (busy.End > cursor) cursor = busy.End;
        }
        return cursor.AddMinutes(durationMinutes) <= windowEnd ? (cursor, cursor.AddMinutes(durationMinutes)) : null;
    }

    private static string BuildMeetingReply(string person, int minutes, DateTimeOffset start, NekomataWorkspace workspace, Guid? threadId)
    {
        var firstName = person.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hi," : $"Hi {firstName},";
        var context = threadId is Guid id ? workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == id)?.Title : null;
        return $"{greeting}\n\nI can do {start:HH:mm}–{start.AddMinutes(minutes):HH:mm} on {start:dddd} to go through " +
               $"{(string.IsNullOrWhiteSpace(context) ? "this" : context)}. Does that work for you?\n\nThanks,";
    }

    private void Classify(AnticipatedAction item)
    {
        var decision = authority.Evaluate(new GuardianActionAuthorityRequest
        {
            ActionType = item.ProposedAction, Title = item.ProposedAction, Reason = item.Reason,
            Confidence = item.Confidence, IsDeterministic = true, Risk = GuardianActionRisk.Low,
            Reversibility = GuardianActionReversibility.Reversible, Evidence = item.Evidence,
            SourceEventId = item.SourceEventId, CorrelationId = item.Id,
            UndoDescription = "Remove the prepared internal artefact or restore the previous derived state."
        });
        item.Authority = decision.Authority;
        item.Status = item.ProposedAction == "WaitForDependency"
            ? AnticipatedActionStatus.Waiting
            : decision.Authority switch
            {
                GuardianActionAuthority.Prepare when !string.IsNullOrWhiteSpace(item.PreparedContent) => AnticipatedActionStatus.Prepared,
                GuardianActionAuthority.Prepare => AnticipatedActionStatus.Proposed,
                GuardianActionAuthority.ApprovalRequired => AnticipatedActionStatus.ApprovalRequired,
                GuardianActionAuthority.AutoExecute => AnticipatedActionStatus.ReadyToExecute,
                _ => AnticipatedActionStatus.Proposed
            };
    }

    private static void AddMeetingPreparation(NekomataWorkspace workspace, IEnumerable<CalendarEvent> calendar,
        DateTimeOffset now, ICollection<AnticipatedAction> queue)
    {
        foreach (var meeting in calendar.Where(x => !x.IsAllDay && !x.IsNekomataManaged &&
                     x.Start > now && x.Start <= now.AddHours(24)))
        {
            var context = workspace.CalendarThreadContexts.FirstOrDefault(x =>
                x.CalendarEventId == meeting.Id && x.LinkStatus == WorkThreadLinkStatus.Confirmed && x.ThreadId is not null);
            if (context?.ThreadId is not Guid threadId) continue;
            var thread = workspace.WorkThreadSummaries.FirstOrDefault(x => x.ThreadId == threadId);
            var evidence = new List<string> { "Confirmed calendar-to-Work-Thread relationship", $"Meeting starts {meeting.Start:ddd HH:mm}" };
            if (thread is not null)
            {
                if (!string.IsNullOrWhiteSpace(thread.HighestImpactDependency)) evidence.Add($"Dependency: {thread.HighestImpactDependency}");
                if (thread.RecentChanges.Count > 0) evidence.Add($"Recent changes available: {thread.RecentChanges.Count}");
            }
            var action = Create("UpcomingMeeting", meeting.Id, threadId, "PrepareMeetingContext",
                $"Prepare the confirmed Work Thread context before {meeting.Subject}.", evidence, 100,
                "Reduces meeting preparation time and surfaces unresolved context.", now, meeting.Start.AddMinutes(-15), meeting.End);
            action.PreparedContent = BuildMeetingBrief(workspace, meeting, threadId, thread);
            queue.Add(action);
        }
    }

    private static string BuildMeetingBrief(NekomataWorkspace workspace, CalendarEvent meeting, Guid threadId,
        GuardianWorkThreadSummary? thread)
    {
        var requirements = workspace.ProcessRequirements.Where(x => x.WorkThreadId == threadId &&
            x.State is not RequirementState.Satisfied and not RequirementState.Waived and not RequirementState.NotApplicable)
            .OrderBy(x => x.Gate).ThenBy(x => x.Name).ToList();
        var attendees = meeting.Attendees.Append(meeting.Organiser).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lines = new List<string>
        {
            $"MEETING PREPARATION · {meeting.Subject}",
            $"When: {meeting.Start:dddd dd MMMM, HH:mm}–{meeting.End:HH:mm}",
            $"Work Thread: {thread?.Title ?? "Confirmed thread; summary unavailable"}",
            $"Project: {(string.IsNullOrWhiteSpace(thread?.PrimaryProject) ? "No primary project recorded" : thread.PrimaryProject)}",
            $"People: {(attendees.Count == 0 ? "No attendees recorded" : string.Join(", ", attendees))}",
            "",
            "CURRENT STATE",
            $"Active tasks: {thread?.ActiveTasks ?? 0}",
            $"Open loops: {thread?.OpenLoops ?? 0}",
            $"Remaining effort: {thread?.RemainingEffortLabel ?? "Unknown"}",
            $"Highest-impact dependency: {(string.IsNullOrWhiteSpace(thread?.HighestImpactDependency) ? "None recorded" : thread.HighestImpactDependency)}",
            "",
            "READINESS",
            requirements.Count == 0 ? "No outstanding process requirements are currently known." :
                string.Join("\n", requirements.Select(x => $"• {x.Name} — {x.State} — {x.Gate} gate: {x.Reason}")),
            "",
            "RECENT CONTEXT"
        };
        var recent = (thread?.RecentChanges ?? []).Take(3).Concat((thread?.TeamsConversations ?? []).Take(2)).ToList();
        lines.Add(recent.Count == 0 ? "No recent confirmed changes or Teams context recorded." :
            string.Join("\n", recent.Select(x => $"• {x}")));
        lines.Add("");
        lines.Add("KEY EVIDENCE");
        var keyEvidence = (thread?.KeyEvidence ?? []).Take(4).ToList();
        lines.Add(keyEvidence.Count == 0 ? "No additional key evidence recorded." :
            string.Join("\n", keyEvidence.Select(x => $"• {x}")));
        lines.Add("");
        lines.Add("LIKELY DECISIONS / QUESTIONS");
        if (requirements.Count > 0) lines.Add("• Confirm how each outstanding readiness requirement will be satisfied.");
        if (!string.IsNullOrWhiteSpace(thread?.HighestImpactDependency)) lines.Add($"• Decide whether to address: {thread.HighestImpactDependency}.");
        if (requirements.Count == 0 && string.IsNullOrWhiteSpace(thread?.HighestImpactDependency))
            lines.Add("• No deterministic decision requirement has been identified.");
        return string.Join("\n", lines);
    }

    private static void AddReadinessChecks(NekomataWorkspace workspace, IEnumerable<CalendarEvent> calendar,
        DateTimeOffset now, ICollection<AnticipatedAction> queue)
    {
        foreach (var meeting in calendar.Where(x => !x.IsNekomataManaged &&
                     x.Start > now && x.Start <= now.AddDays(2)))
        {
            var context = workspace.CalendarThreadContexts.FirstOrDefault(x => x.CalendarEventId == meeting.Id && x.ThreadId is not null);
            if (context?.ThreadId is not Guid threadId) continue;
            var outstanding = workspace.ProcessRequirements.Where(x => x.WorkThreadId == threadId &&
                x.State is not RequirementState.Satisfied and not RequirementState.Waived and not RequirementState.NotApplicable).ToList();
            if (outstanding.Count == 0) continue;
            queue.Add(Create("UpcomingCommitment", meeting.Id, threadId, "CheckReadinessGate",
                $"Check readiness before {meeting.Subject}; {outstanding.Count} requirement(s) remain outstanding.",
                outstanding.Select(x => $"{x.Name}: {x.State} ({x.Gate} gate)").ToList(), 100,
                "Prevents work progressing without required process evidence.", now, now, meeting.Start));
        }
    }

    private static void AddThreadNextActions(NekomataWorkspace workspace, DateTimeOffset now,
        ICollection<AnticipatedAction> queue)
    {
        foreach (var next in workspace.WorkThreadNextActions)
        {
            // Keep generic idle predictions queryable without making the operational queue noisy.
            if (next.ActionType == "AwaitNewEvidence") continue;
            var actionType = next.Disposition == WorkThreadNextActionDisposition.Wait ? "WaitForDependency" : next.ActionType;
            queue.Add(Create("WorkThreadNextAction", next.Id.ToString("N"), next.WorkThreadId, actionType,
                next.Reason, next.Evidence, next.Confidence,
                next.Disposition == WorkThreadNextActionDisposition.Wait
                    ? "Records why no intervention should occur yet."
                    : "Makes the Work Thread's next permitted operational transition explicit.",
                now, next.Disposition == WorkThreadNextActionDisposition.Wait ? next.ReconsiderAt : now,
                next.ReconsiderAt?.AddDays(1) ?? now.AddDays(1)));
        }
    }

    private static AnticipatedAction Create(string trigger, string key, Guid? threadId, string action, string reason,
        List<string> evidence, int confidence, string impact, DateTimeOffset now, DateTimeOffset? executeAfter, DateTimeOffset expires)
    {
        var id = StableId($"{trigger}:{key}:{action}");
        return new AnticipatedAction { Id = id, SourceEventId = id, TriggerType = trigger, TriggerKey = key,
            WorkThreadId = threadId, ProposedAction = action, Reason = reason, Evidence = evidence,
            Confidence = confidence, Impact = impact, CreatedAt = now, ExecuteAfter = executeAfter, ExpiresAt = expires };
    }

    private static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
}
