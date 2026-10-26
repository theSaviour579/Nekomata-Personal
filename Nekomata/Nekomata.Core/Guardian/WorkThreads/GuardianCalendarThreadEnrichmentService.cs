using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Core.Guardian.Authority;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.WorkThreads;

public sealed partial class GuardianCalendarThreadEnrichmentService(
    IGuardianWorkThreadRepository threads,
    IGuardianCalendarEffortRepository effort,
    WorkdayJournalService journal,
    IGuardianOpenLoopRepository? openLoopRepository = null,
    GuardianOpenLoopService? openLoopService = null,
    GuardianActionAuthorityEngine? authority = null)
{
    public async Task EnrichAsync(NekomataWorkspace workspace, IReadOnlyCollection<CalendarEvent> events, DateTimeOffset now)
    {
        workspace.CalendarThreadContexts = [];
        workspace.MissingWorkCandidates.RemoveAll(x => x.SourceType == "CalendarEvent");
        foreach (var calendarEvent in events.Where(x => !x.IsAllDay))
        {
            var context = await CorrelateAsync(workspace, calendarEvent, now);
            workspace.CalendarThreadContexts.Add(context);
            if (context.ThreadId is not Guid threadId || context.LinkStatus != WorkThreadLinkStatus.Confirmed) continue;

            var duration = Math.Max(0, (int)(calendarEvent.End - calendarEvent.Start).TotalMinutes);
            if (authority is not null)
            {
                var authorityDecision = await authority.EvaluateAndRecordAsync(new GuardianActionAuthorityRequest
                {
                    ActionType="AttributeMeetingEffort",Title=$"Attribute meeting effort: {calendarEvent.Subject}",
                    Reason="The calendar event has a confirmed Work Thread relationship.",Confidence=context.Confidence,
                    IsDeterministic=true,Risk=GuardianActionRisk.Low,Reversibility=GuardianActionReversibility.Reversible,
                    UndoDescription="Remove or replace the calendar-effort attribution.",
                    Evidence=[$"Confirmed CalendarEvent link ({context.Confidence}%)",context.Explanation],
                    SourceEventId=StableId("calendar",calendarEvent.Id),CorrelationId=threadId,
                    ProjectId=workspace.WorkThreads.FirstOrDefault(x=>x.Id==threadId)?.PrimaryProjectId,TaskId=context.TaskId
                });
                if(!authorityDecision.CanExecute) continue;
            }
            await effort.UpsertAsync(new GuardianCalendarEffort
            {
                CalendarEventId = calendarEvent.Id, ThreadId = threadId, TaskId = context.TaskId,
                Subject = calendarEvent.Subject, Start = calendarEvent.Start, End = calendarEvent.End,
                DurationMinutes = duration, State = calendarEvent.End <= now ? "Recorded" : "Planned",
                SourceEventId = StableId("calendar", calendarEvent.Id)
            });
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = StableId("calendar-effort", calendarEvent.Id), CorrelationId = threadId,
                OccurredAt = now, EventType = "CalendarEffortAttributed", Category = "WorkThread",
                Title = calendarEvent.Subject,
                Narrative = $"Attributed {duration} calendar minutes to Work Thread {threadId:N}" +
                            (context.TaskId is long taskId ? $" through task {taskId}; task effort was not double-counted." : "."),
                ExternalId = calendarEvent.Id, Outcome = calendarEvent.End <= now ? "Recorded" : "Planned",
                Significance = 55
            });
            var recorded = calendarEvent.End <= now;
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = StableId(recorded ? "calendar-effort-recorded" : "calendar-effort-planned", calendarEvent.Id),
                CorrelationId = threadId, OccurredAt = recorded ? calendarEvent.End : now,
                EventType = recorded ? "CalendarEffortRecorded" : "CalendarEffortPlanned", Category = "WorkThread",
                Title = calendarEvent.Subject,
                Narrative = recorded
                    ? $"Recorded {duration} minutes of meeting effort against the confirmed Work Thread."
                    : $"Planned {duration} minutes of meeting effort against the confirmed Work Thread.",
                ProjectId = workspace.WorkThreads.FirstOrDefault(x => x.Id == threadId)?.PrimaryProjectId,
                ExternalId = calendarEvent.Id, Outcome = recorded ? "Recorded" : "Planned", Significance = 55
            });

            context.DurationMinutes = duration;
            context.IsRecorded = recorded;
            context.ResolvedOpenLoops = await ResolveMeetingLoopsAsync(workspace, calendarEvent, threadId, now);
            context.MissingRequiredWork = context.TaskId is null && RepresentsRequiredWork(calendarEvent);
            if (!context.MissingRequiredWork) continue;
            workspace.MissingWorkCandidates.Add(new GuardianMissingWorkCandidate
            {
                SourceType = "CalendarEvent", SourceKey = calendarEvent.Id, Title = calendarEvent.Subject,
                ThreadId = threadId, Confidence = context.Confidence,
                Reason = "Confirmed Work Thread meeting has no associated task or external work item."
            });
            await journal.RecordIfAbsentAsync(new WorkdayEvent
            {
                EventId = StableId("calendar-missing-work", calendarEvent.Id), CorrelationId = threadId,
                OccurredAt = now, EventType = "UnboundRequiredWorkDetected", Category = "WorkThread",
                Title = calendarEvent.Subject, Narrative = "Meeting represents understood work but no task is linked; review required.",
                ExternalId = calendarEvent.Id, Outcome = "Candidate", Significance = 65
            });
        }
        workspace.WorkThreads = await threads.GetActiveAsync();
    }

    private async Task<int> ResolveMeetingLoopsAsync(NekomataWorkspace workspace, CalendarEvent calendarEvent,
        Guid threadId, DateTimeOffset now)
    {
        if (openLoopRepository is null || openLoopService is null || calendarEvent.Start < now.AddDays(-1)) return 0;
        var projectId = workspace.WorkThreads.FirstOrDefault(x => x.Id == threadId)?.PrimaryProjectId;
        if (projectId is null) return 0;
        var participants = calendarEvent.Attendees.Append(calendarEvent.Organiser).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var resolved = 0;
        foreach (var loop in (await openLoopRepository.GetOpenAsync()).Where(x => x.Type == OpenLoopType.MeetingRequired &&
                     x.ProjectId == projectId && !string.IsNullOrWhiteSpace(x.WaitingOn)))
        {
            if (!participants.Any(x => PersonMatches(x, loop.WaitingOn))) continue;
            if(authority is not null)
            {
                var authorityDecision=await authority.EvaluateAndRecordAsync(new GuardianActionAuthorityRequest
                {
                    ActionType="ResolveOpenLoop",Title=$"Resolve meeting requirement: {loop.Title}",
                    Reason=$"A confirmed-thread meeting includes the expected person, {loop.WaitingOn}.",Confidence=100,
                    IsDeterministic=true,Risk=GuardianActionRisk.Low,Reversibility=GuardianActionReversibility.Compensatable,
                    UndoDescription="Reopen the loop if the scheduled meeting does not satisfy the requirement.",
                    Evidence=[$"Project {projectId}",$"Attendee {loop.WaitingOn}",$"Calendar event {calendarEvent.Id}"],
                    SourceEventId=StableId("calendar",calendarEvent.Id),CorrelationId=loop.SourceEventId,ProjectId=projectId,TaskId=loop.TaskId
                });
                if(!authorityDecision.CanExecute) continue;
            }
            var result = await openLoopService.ResolveFromEvidenceAsync(new GuardianLoopEvidence
            {
                EventId = StableId("calendar-meeting-loop", $"{calendarEvent.Id}:{loop.Id}"), OpenLoopId = loop.Id,
                Kind = "MeetingScheduled", OccurredAt = now, CalendarEventId = calendarEvent.Id,
                ProjectId = projectId, Summary = $"Meeting scheduled with {loop.WaitingOn}: {calendarEvent.Subject}"
            });
            resolved += result.Count;
        }
        return resolved;
    }

    private static bool PersonMatches(string participant, string waitingOn)
    {
        var participantTerms = Regex.Matches(participant, "[A-Za-z]+") .Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waitingTerms = Regex.Matches(waitingOn, "[A-Za-z]+") .Select(x => x.Value).ToList();
        return waitingTerms.Count > 0 && waitingTerms.All(participantTerms.Contains);
    }

    private async Task<GuardianCalendarThreadContext> CorrelateAsync(NekomataWorkspace workspace, CalendarEvent calendarEvent, DateTimeOffset now)
    {
        var sourceEvent = StableId("calendar", calendarEvent.Id);
        var confirmedLink = await threads.FindLinkAsync("CalendarEvent", calendarEvent.Id, WorkThreadLinkStatus.Confirmed);
        if (confirmedLink is not null)
        {
            var existingEffort = (await effort.GetForThreadAsync(confirmedLink.ThreadId))
                .FirstOrDefault(x => x.CalendarEventId == calendarEvent.Id);
            return new GuardianCalendarThreadContext
            {
                CalendarEventId = calendarEvent.Id, ThreadId = confirmedLink.ThreadId,
                LinkStatus = WorkThreadLinkStatus.Confirmed, Confidence = confirmedLink.Confidence,
                TaskId = existingEffort?.TaskId, Explanation = confirmedLink.LinkMethod
            };
        }

        var taskMatch = TaskMarker().Match(calendarEvent.BodyPreview);
        if (taskMatch.Success && long.TryParse(taskMatch.Groups["id"].Value, out var taskId))
        {
            var thread = FindThread(workspace, "Task", taskId.ToString());
            if (thread is not null)
                return await LinkAsync(thread.Id, calendarEvent, 100, WorkThreadLinkAuthority.Explicit,
                    WorkThreadLinkStatus.Confirmed, "Calendar.TaskMarker", sourceEvent, taskId,
                    [("ExplicitTaskMarker", taskId.ToString(), 100)]);
        }

        var projectMatch = ProjectMarker().Match(calendarEvent.BodyPreview);
        if (projectMatch.Success && long.TryParse(projectMatch.Groups["id"].Value, out var projectId))
        {
            var thread = workspace.WorkThreads.FirstOrDefault(x => x.PrimaryProjectId == projectId);
            if (thread is not null)
                return await LinkAsync(thread.Id, calendarEvent, 100, WorkThreadLinkAuthority.Explicit,
                    WorkThreadLinkStatus.Confirmed, "Calendar.ProjectMarker", sourceEvent, null,
                    [("ExplicitProjectMarker", projectId.ToString(), 100)]);
        }

        GuardianCalendarThreadContext? best = null;
        var subjectTerms = CalendarTerms(calendarEvent.Subject).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in workspace.WorkThreads.Where(x => x.PrimaryProjectId is not null))
        {
            var project = workspace.Projects.FirstOrDefault(x => x.Id == thread.PrimaryProjectId);
            if (project is null) continue;
            var projectTerms = CalendarTerms(project.Name);
            var matched = projectTerms.Where(subjectTerms.Contains).ToList();
            if (matched.Count == 0) continue;
            var confidence = matched.Count == projectTerms.Count ? 80 : 60;
            var evidenceType = matched.Count == projectTerms.Count ? "ProjectTermsMatched" : "PartialProjectTerms";
            var candidate = await LinkAsync(thread.Id, calendarEvent, confidence, WorkThreadLinkAuthority.Deterministic,
                WorkThreadLinkStatus.Candidate, evidenceType, sourceEvent, null,
                [(evidenceType, string.Join(", ", matched), confidence)]);
            if (best is null || candidate.Confidence > best.Confidence) best = candidate;
        }
        return best ?? new GuardianCalendarThreadContext
        {
            CalendarEventId = calendarEvent.Id, Explanation = "No deterministic Work Thread evidence found."
        };
    }

    public async Task ConfirmThreadAsync(CalendarEvent calendarEvent, Guid threadId)
    {
        var sourceEvent = StableId("calendar", calendarEvent.Id);
        await LinkAsync(threadId, calendarEvent, 100, WorkThreadLinkAuthority.Explicit,
            WorkThreadLinkStatus.Confirmed, "UserConfirmed", sourceEvent, null,
            [("UserConfirmation", threadId.ToString("N"), 100)]);
        await journal.RecordIfAbsentAsync(new WorkdayEvent
        {
            EventId = StableId("calendar-thread-user-confirmed", calendarEvent.Id), CorrelationId = threadId,
            OccurredAt = DateTimeOffset.Now, EventType = "CalendarWorkThreadConfirmed", Category = "WorkThread",
            Title = calendarEvent.Subject, Narrative = "User selected and confirmed the Work Thread relationship.",
            ExternalId = calendarEvent.Id, Outcome = "Confirmed", Significance = 70,
            UserDecision = "Approved"
        });
    }

    public async Task ConfirmTaskAsync(CalendarEvent calendarEvent, Guid threadId, long taskId)
    {
        var sourceEvent = StableId("calendar-task", $"{calendarEvent.Id}:{taskId}");
        var taskLink = await threads.UpsertLinkAsync(new GuardianWorkThreadLink
        {
            ThreadId = threadId, EntityType = "Task", EntityKey = taskId.ToString(),
            RelationshipType = "BelongsTo", Confidence = 100, Authority = WorkThreadLinkAuthority.Explicit,
            Status = WorkThreadLinkStatus.Confirmed, LinkMethod = "Calendar.UserSelectedTask", SourceEventId = sourceEvent
        });
        await threads.AddEvidenceAsync(new GuardianCorrelationEvidence
        {
            LinkId = taskLink.Id, EvidenceType = "UserConfirmation", Value = calendarEvent.Id,
            Weight = 100, SourceEventId = sourceEvent
        });
        await effort.UpsertAsync(new GuardianCalendarEffort
        {
            CalendarEventId = calendarEvent.Id, ThreadId = threadId, TaskId = taskId,
            Subject = calendarEvent.Subject, Start = calendarEvent.Start, End = calendarEvent.End,
            DurationMinutes = Math.Max(0, (int)(calendarEvent.End - calendarEvent.Start).TotalMinutes),
            State = calendarEvent.End <= DateTimeOffset.Now ? "Recorded" : "Planned", SourceEventId = sourceEvent
        });
        await journal.RecordIfAbsentAsync(new WorkdayEvent
        {
            EventId = StableId("calendar-task-user-confirmed", calendarEvent.Id), CorrelationId = threadId,
            OccurredAt = DateTimeOffset.Now, EventType = "CalendarTaskConfirmed", Category = "WorkThread",
            Title = calendarEvent.Subject, Narrative = $"User associated the meeting with task {taskId}.",
            ExternalId = calendarEvent.Id, TaskId = taskId, Outcome = "Confirmed", Significance = 70,
            UserDecision = "Approved"
        });
    }

    private async Task<GuardianCalendarThreadContext> LinkAsync(Guid threadId, CalendarEvent calendarEvent,
        int initialConfidence, WorkThreadLinkAuthority authority, WorkThreadLinkStatus status, string method,
        Guid sourceEvent, long? taskId, IReadOnlyCollection<(string Type, string Value, int Weight)> evidence)
    {
        var link = await threads.UpsertLinkAsync(new GuardianWorkThreadLink
        {
            ThreadId = threadId, EntityType = "CalendarEvent", EntityKey = calendarEvent.Id,
            RelationshipType = "Schedules", Confidence = initialConfidence, Authority = authority,
            Status = status, LinkMethod = method, SourceEventId = sourceEvent
        });
        foreach (var item in evidence)
            await threads.AddEvidenceAsync(new GuardianCorrelationEvidence
            {
                LinkId = link.Id, EvidenceType = item.Type, Value = item.Value,
                Weight = item.Weight, SourceEventId = sourceEvent
            });
        var evaluation = authority == WorkThreadLinkAuthority.Explicit
            ? new GuardianCorrelationEvaluation(100, WorkThreadLinkStatus.Confirmed, method)
            : GuardianCorrelationPolicy.Evaluate(await threads.GetEvidenceAsync(link.Id));
        if (authority != WorkThreadLinkAuthority.Explicit)
            await threads.UpdateEvaluationAsync(link.Id, evaluation.Confidence, evaluation.Status, evaluation.Explanation);
        await journal.RecordIfAbsentAsync(new WorkdayEvent
        {
            EventId = StableId("calendar-thread-link", calendarEvent.Id), CorrelationId = threadId,
            OccurredAt = DateTimeOffset.Now, EventType = "CalendarLinkedToWorkThread", Category = "WorkThread",
            Title = calendarEvent.Subject,
            Narrative = $"{evaluation.Status} calendar relationship via {evaluation.Explanation} at {evaluation.Confidence}% confidence.",
            ExternalId = calendarEvent.Id, Outcome = evaluation.Status.ToString(), Significance = 50
        });
        return new GuardianCalendarThreadContext
        {
            CalendarEventId = calendarEvent.Id, ThreadId = threadId, LinkStatus = evaluation.Status,
            Confidence = evaluation.Confidence, TaskId = taskId, Explanation = evaluation.Explanation
        };
    }

    private static GuardianWorkThread? FindThread(NekomataWorkspace workspace, string entityType, string key) =>
        workspace.WorkThreads.FirstOrDefault(x => x.Links.Any(link => link.EntityType == entityType &&
            link.EntityKey.Equals(key, StringComparison.OrdinalIgnoreCase) && link.Status == WorkThreadLinkStatus.Confirmed));

    private static bool RepresentsRequiredWork(CalendarEvent value) => value.Subject.Contains("change", StringComparison.OrdinalIgnoreCase) ||
        value.Subject.Contains("review", StringComparison.OrdinalIgnoreCase) || value.Subject.Contains("workshop", StringComparison.OrdinalIgnoreCase) ||
        value.Subject.Contains("implementation", StringComparison.OrdinalIgnoreCase) || value.Subject.Contains("testing", StringComparison.OrdinalIgnoreCase);

    private static List<string> CalendarTerms(string value) => Regex.Matches(value, "[A-Za-z0-9]+")
        .Select(x => x.Value).Where(x => x.Length >= 3 &&
            !new[] { "project", "program", "meeting", "review" }.Contains(x, StringComparer.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Guid StableId(string type, string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}:{key}"))[..16]);
    [GeneratedRegex(@"NEKOMATA:(?:TASK:)?(?<id>\d+)", RegexOptions.IgnoreCase)] private static partial Regex TaskMarker();
    [GeneratedRegex(@"NEKOMATA:PROJECT:(?<id>\d+)", RegexOptions.IgnoreCase)] private static partial Regex ProjectMarker();
}
