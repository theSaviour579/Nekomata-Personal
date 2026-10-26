using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.WorkThreads;

public sealed class GuardianWorkThreadEnrichmentService(
    IGuardianOpenLoopRepository openLoops,
    GuardianOpenLoopAgeService ageService,
    IWorkdayEventRepository journal,
    IGuardianCalendarEffortRepository? calendarEffort = null,
    IContextSignalRepository? contextSignals = null)
{
    public async Task<IReadOnlyList<GuardianWorkThreadSummary>> BuildAsync(NekomataWorkspace workspace, DateTimeOffset now)
    {
        var loops = await openLoops.GetOpenAsync();
        var aged = (await ageService.EvaluateAsync(now)).ToDictionary(x => x.OpenLoop.Id);
        var events = await journal.GetBetweenAsync(now.AddDays(-30), now, 2000);
        var signals = contextSignals is null ? [] : await contextSignals.GetCurrentAsync(2000);
        var summaries = new List<GuardianWorkThreadSummary>();

        foreach (var thread in workspace.WorkThreads)
        {
            var confirmed = thread.Links.Where(x => x.Status == WorkThreadLinkStatus.Confirmed).ToList();
            var taskIds = confirmed.Where(x => x.EntityType == "Task" && long.TryParse(x.EntityKey, out _))
                .Select(x => long.Parse(x.EntityKey)).ToHashSet();
            var tasks = workspace.Tasks.Where(x => taskIds.Contains(x.Id)).ToList();
            var loopIds = confirmed.Where(x => x.EntityType == "OpenLoop" && Guid.TryParse(x.EntityKey, out _))
                .Select(x => Guid.Parse(x.EntityKey)).ToHashSet();
            var threadLoops = loops.Where(x => loopIds.Contains(x.Id)).ToList();
            var project = thread.PrimaryProjectId is long projectId
                ? workspace.Projects.FirstOrDefault(x => x.Id == projectId) : null;
            var threadEvents = events.Where(x => project is not null && x.ProjectId == project.Id &&
                x.EventType is not "TeamsContextCorrelated" and not "TeamsContextReviewRequired").ToList();
            var teamsConversations = BuildTeamsEpisodes(signals, thread.Id, now);
            var upcomingTask = tasks.Where(x => x.DueAt is not null && x.DueAt >= now.LocalDateTime)
                .OrderBy(x => x.DueAt).FirstOrDefault();
            var meetings = calendarEffort is null ? [] : await calendarEffort.GetForThreadAsync(thread.Id);
            var upcomingMeeting = meetings.Where(x => x.End > now).OrderBy(x => x.Start).FirstOrDefault();
            var dependency = threadLoops.OrderByDescending(x => aged.TryGetValue(x.Id, out var value) && value.ChaseMature)
                .ThenBy(x => x.CreatedAt).FirstOrDefault();
            var lastActivity = confirmed.Select(x => (DateTimeOffset?)x.CreatedAt)
                .Concat(threadEvents.Select(x => (DateTimeOffset?)x.OccurredAt)).Max();

            var summary = new GuardianWorkThreadSummary
            {
                ThreadId = thread.Id, Title = thread.Title, PrimaryProject = project?.Name ?? "",
                ActiveTasks = tasks.Count, Meetings = Count(confirmed, "CalendarEvent"),
                OpenLoops = threadLoops.Count, BlockingDependencies = threadLoops.Count,
                RecentCommunications = Count(confirmed, "Email", "TeamsMessage", "Message", "Chat"),
                PlannedEffortMinutes = tasks.Sum(x => Math.Max(0, x.EstimatedMinutes)) +
                    meetings.Where(x => x.State == "Planned" && x.TaskId is null).Sum(x => x.DurationMinutes),
                RecordedEffortMinutes = tasks.Sum(x => Math.Max(0, x.ActualMinutes)) +
                    meetings.Where(x => x.State == "Recorded" && x.TaskId is null).Sum(x => x.DurationMinutes),
                RemainingEffortMinutes = tasks.Sum(x => Math.Max(0, x.EstimatedMinutes - x.ActualMinutes)),
                PlannedMeetingMinutes = meetings.Where(x => x.State == "Planned").Sum(x => x.DurationMinutes),
                RecordedMeetingMinutes = meetings.Where(x => x.State == "Recorded").Sum(x => x.DurationMinutes),
                AttributedMeetingMinutes = meetings.Where(x => x.TaskId is not null).Sum(x => x.DurationMinutes),
                LastMeaningfulActivity = lastActivity,
                NextCommitment = upcomingMeeting is not null ? $"{upcomingMeeting.Subject} — {upcomingMeeting.Start.LocalDateTime:ddd dd MMM HH:mm}" :
                    upcomingTask?.DueAt is DateTime due ? $"{upcomingTask.Title} — {due:ddd dd MMM HH:mm}" : "",
                HighestImpactDependency = dependency is null ? "" :
                    (string.IsNullOrWhiteSpace(dependency.WaitingOn) ? dependency.Title : $"Waiting on {dependency.WaitingOn}"),
                RecentChanges = threadEvents.Where(x => x.OccurredAt >= now.AddDays(-1)).OrderByDescending(x => x.OccurredAt)
                    .Select(x => x.Title).Distinct().Take(10).ToList(),
                TeamsConversations = teamsConversations,
                CompletedInterventions = threadEvents.Where(x => x.EventType is "OpenLoopChaseSent" or "OpenLoopResolved" or "ObjectiveCompleted")
                    .OrderByDescending(x => x.OccurredAt).Select(x => x.Narrative).Take(10).ToList(),
                KeyEvidence = confirmed.OrderByDescending(x => x.Confidence)
                    .Select(x => $"{x.EntityType} → {x.RelationshipType} · {x.Authority} · {x.Confidence}% · {x.LinkMethod}")
                    .Take(8).ToList()
            };
            summary.Impact = new GuardianWorkThreadImpact
            {
                IsBlockingCurrentObjective = threadLoops.Count > 0 && workspace.CurrentMission?.ProjectId == thread.PrimaryProjectId,
                IsBlockingActiveTask = threadLoops.Any(x => x.TaskId is not null && taskIds.Contains(x.TaskId.Value)),
                HasUpcomingCommitment = upcomingTask is not null || upcomingMeeting is not null,
                HasMatureOpenLoop = threadLoops.Any(x => aged.TryGetValue(x.Id, out var value) && value.ChaseMature),
                HasUnboundRequiredWork = workspace.MissingWorkCandidates.Any(x => x.ThreadId == thread.Id) ||
                    thread.Links.Where(x => x.Status == WorkThreadLinkStatus.Candidate)
                        .Any(link => workspace.UnboundContext.Any(x => x.EntityType == link.EntityType && x.EntityKey == link.EntityKey)),
                IsStale = lastActivity is not null && GuardianOpenLoopAgeService.WorkingDaysBetween(lastActivity.Value, now) >= 5,
                ProjectPriority = project?.Priority ?? "", BusinessValue = project?.EstimatedBusinessValue ?? 0
            };
            summaries.Add(summary);
        }
        return summaries.OrderByDescending(x => x.Impact.IsBlockingCurrentObjective)
            .ThenByDescending(x => x.Impact.HasMatureOpenLoop).ThenByDescending(x => x.Impact.BusinessValue).ToList();
    }

    private static int Count(IEnumerable<GuardianWorkThreadLink> links, params string[] types) =>
        links.Count(x => types.Contains(x.EntityType, StringComparer.OrdinalIgnoreCase));

    private static List<string> BuildTeamsEpisodes(IEnumerable<ContextSignal> signals, Guid threadId, DateTimeOffset now)
    {
        var result = new List<(DateTimeOffset Last, string Text)>();
        foreach (var chat in signals.Where(x => x.Source == "Microsoft Teams" && x.ObservedAt >= now.AddDays(-30))
                     .GroupBy(x => string.IsNullOrWhiteSpace(x.ConversationId) ? x.ExternalId : x.ConversationId))
        {
            var episode = new List<ContextSignal>();
            foreach (var message in chat.OrderBy(x => x.ObservedAt))
            {
                if (episode.Count > 0 && message.ObservedAt - episode[^1].ObservedAt > TimeSpan.FromMinutes(20))
                {
                    AddIfRelevant(episode);
                    episode = [];
                }
                episode.Add(message);
            }
            AddIfRelevant(episode);
        }
        return result.OrderByDescending(x => x.Last).Take(5).Select(x => x.Text).ToList();

        void AddIfRelevant(List<ContextSignal> episode)
        {
            if (episode.Count == 0 || !episode.Any(x => x.WorkThreadId == threadId &&
                    x.Disposition is ContextSignalDisposition.Correlated or ContextSignalDisposition.NeedsReview)) return;
            result.Add((episode[^1].ObservedAt, string.Join("\n", episode.Select(x =>
                $"{x.ObservedAt.LocalDateTime:ddd HH:mm} · {(string.IsNullOrWhiteSpace(x.Person) ? "Unknown sender" : x.Person)}: {x.Title}"))));
        }
    }
}
