using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Planning;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using System.Collections.ObjectModel;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GuardianChatTurn>
        ChatHistory
    { get; } = [];

    [ObservableProperty]
    private GuardianDashboardRecommendation?
        topRecommendation;

    [ObservableProperty]
    private GuardianActionResponse?
        pendingGuardianAction;

    [ObservableProperty]
    private NekomataProject?
        pendingGuardianProject;

    [ObservableProperty]
    private bool guardianProposalVisible;

    [ObservableProperty]
    private bool guardianPanelExpanded;

    [ObservableProperty]
    private string guardianPanelTitle = "GUARDIAN";

    [ObservableProperty]
    private string chatInput = "";

    [ObservableProperty]
    private string guardianResponse = "";

    [ObservableProperty]
    private bool guardianBusy;

    [RelayCommand]
    private async Task SendGuardianMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput) ||
            GuardianBusy)
        {
            return;
        }

        var userMessage =
            ChatInput.Trim();

        ChatInput = "";
        GuardianBusy = true;
        GuardianPanelExpanded = true;

        ChatHistory.Add(
            new GuardianChatTurn
            {
                Role = "user",
                Content = userMessage
            });

        try
        {
            var calendarBriefing = await TryBuildCalendarDayBriefingAsync(userMessage);
            if (calendarBriefing is not null)
            {
                GuardianResponse = calendarBriefing;
                PendingGuardianAction = null;
                PendingGuardianProject = null;
                GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = calendarBriefing });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                _ = SpeakGuardianAsync(calendarBriefing);
                return;
            }

            var memoryRepository =
       _services.GetRequiredService<
           IGuardianMemoryRepository>();

            var response = await TryBuildDeterministicCalendarPlanAsync(userMessage);
            if (response is null)
            {
                if (!EnsurePersonalAiConfigured())
                {
                    ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                    GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                    return;
                }
                var request = new GuardianConversationRequest
                {
                    UserMessage = await AddCalendarContextAsync(userMessage),
                    Workspace = Workspace,
                    CurrentTime = DateTime.Now,
                    Conversation = ChatHistory.ToList()
                };
                response = await _guardianConversationService.AskAsync(request);
            }

            GuardianResponse =
                response.Message;

            PendingGuardianAction =
                response;

            if (response.ProjectId is long projectId)
            {
                PendingGuardianProject =
                    Workspace.Projects
                        .FirstOrDefault(p => p.Id == projectId);
            }

            GuardianProposalVisible =
                response.Changes.Count > 0 ||
                response.Tasks.Count > 0;

            ChatHistory.Add(
                new GuardianChatTurn
                {
                    Role = "assistant",
                    Content = response.Message
                });

            GuardianPanelTitle =
                $"GUARDIAN · {ChatHistory.Count} MESSAGES";

            _ = SpeakGuardianAsync(response.Message);

            await RememberImportantConversationAsync(
    memoryRepository,
    userMessage,
    response.Message);

        }
        catch (Exception ex)
        {
            GuardianResponse =
                "Guardian could not complete the request: " +
                ex.Message;

            ChatHistory.Add(
    new GuardianChatTurn
    {
        Role = "assistant",
        Content = GuardianResponse
    });
        }
        finally
        {
            GuardianBusy = false;
        }
    }



    private async Task<GuardianActionResponse?> TryBuildDeterministicCalendarPlanAsync(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var asksToPlan = new[] { "plan", "schedule", "fill", "organise", "organize", "slot", "slotted" }.Any(lower.Contains);
        var calendarScope = new[] { "day", "calendar", "today", "tomorrow", "monday", "tuesday", "wednesday", "thursday", "friday", "when" }.Any(lower.Contains);
        if (!asksToPlan || !calendarScope) return null;

        var dates = ResolvePlanningDates(lower);
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var calendar = _services.GetRequiredService<ICalendarService>();
        var ticketMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\bticket\s*#?\s*(\d+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var requestedTicketId = ticketMatch.Success
            ? ticketMatch.Groups[1].Value
            : null;
        var candidates = Workspace.RankedMissionCandidates
            .Where(item => item.EstimatedMinutes > 0)
            .Where(item => requestedTicketId is null ||
                (item.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(item.SourceRecordId, requestedTicketId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.RequiresImmediateAttention)
            .ThenBy(item => item.Rank)
            .ThenByDescending(item => item.Score)
            .ToList();
        var usedCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var response = new GuardianActionResponse { ActionType = "calendar_plan", Confidence = 100 };
        var narrative = new System.Text.StringBuilder("Guardian built this plan from the live Microsoft 365 calendar and ranked workspace tasks.");

        foreach (var date in dates)
        {
            var offset = TimeZoneInfo.Local.GetUtcOffset(date);
            var workStart = new DateTimeOffset(settings.GetStart(date), offset);
            var workEnd = new DateTimeOffset(settings.GetEnd(date), offset);
            var events = (await calendar.GetEventsAsync(new DateTimeOffset(date.Date, offset), new DateTimeOffset(date.Date.AddDays(1), offset))).ToList();
            var occupied = events.Select(item => item.IsAllDay ? (Start: workStart, End: workEnd) : (Start: item.Start, End: item.End)).ToList();
            if (settings.IncludeLunchBreak && !events.Any(item => item.Subject.Contains("lunch", StringComparison.OrdinalIgnoreCase)))
                occupied.Add((new DateTimeOffset(settings.GetLunchStart(date), offset), new DateTimeOffset(settings.GetLunchEnd(date), offset)));

            var gaps = CalculateFreeWindows(workStart, workEnd, occupied)
                .Where(gap => gap.End - gap.Start >= TimeSpan.FromMinutes(settings.MinimumFocusBlockMinutes)).ToList();
            narrative.AppendLine().AppendLine().AppendLine($"{date:dddd dd MMMM}:");

            foreach (var gap in gaps)
            {
                var cursor = gap.Start;
                while (gap.End - cursor >= TimeSpan.FromMinutes(settings.MinimumFocusBlockMinutes))
                {
                    var candidate = candidates.FirstOrDefault(item =>
                    {
                        var id = item.TaskId is long taskId
                            ? $"Task:{taskId}"
                            : $"{item.SourceType}:{item.SourceRecordId}";
                        return !usedCandidateIds.Contains(id) &&
                            !events.Any(calendarEvent =>
                                calendarEvent.End > cursor &&
                                ((item.TaskId is long existingTaskId &&
                                  (calendarEvent.BodyPreview.Contains($"NEKOMATA:TASK:{existingTaskId}", StringComparison.OrdinalIgnoreCase) ||
                                   calendarEvent.BodyPreview.Contains($"NEKOMATA:{existingTaskId}", StringComparison.OrdinalIgnoreCase))) ||
                                 (item.SourceType.Equals("Halo", StringComparison.OrdinalIgnoreCase) &&
                                  !string.IsNullOrWhiteSpace(item.SourceRecordId) &&
                                  calendarEvent.BodyPreview.Contains($"NEKOMATA:HALO:{item.SourceRecordId}", StringComparison.OrdinalIgnoreCase)) ||
                                 calendarEvent.Subject.Contains(item.Title, StringComparison.OrdinalIgnoreCase)));
                    });
                    if (candidate is null) break;

                    var candidateId = candidate.TaskId is long candidateTaskId
                        ? $"Task:{candidateTaskId}"
                        : $"{candidate.SourceType}:{candidate.SourceRecordId}";
                    var calendarEntityId = candidate.TaskId ?? 0;
                    var available = Math.Max(0, (int)(gap.End - cursor).TotalMinutes);
                    var minutes = Math.Min(Math.Clamp(candidate.EstimatedMinutes, settings.MinimumFocusBlockMinutes, 120), available);
                    if (minutes < settings.MinimumFocusBlockMinutes) break;
                    var blockEnd = cursor.AddMinutes(minutes);
                    response.Changes.Add(new GuardianChange
                    {
                        Selected = true, EntityType = "Calendar", EntityId = calendarEntityId, Property = "CreateFocusBlock",
                        OldValue = string.Empty, NewValue = $"{cursor:O}|{blockEnd:O}|{candidate.Title}",
                        Reason = $"Rank {candidate.Rank}, score {candidate.Score}, {candidate.Priority} priority; fitted to an exact free window.",
                        Confidence = 100
                    });
                    usedCandidateIds.Add(candidateId);
                    narrative.AppendLine($"• {cursor:HH:mm}–{blockEnd:HH:mm} · {candidate.Title}");
                    cursor = blockEnd;
                }
            }
        }

        response.Message = response.Changes.Count == 0
            ? narrative.AppendLine().Append("No unscheduled ranked tasks could be fitted into the available working windows.").ToString().TrimEnd()
            : narrative.AppendLine().Append($"{response.Changes.Count} exact calendar blocks are ready for review. Nothing has been changed yet.").ToString().TrimEnd();
        return response;
    }

    private static IReadOnlyList<DateTime> ResolvePlanningDates(string lower)
    {
        var dates = new List<DateTime>();
        if (lower.Contains("today")) dates.Add(DateTime.Today);
        if (lower.Contains("tomorrow")) dates.Add(DateTime.Today.AddDays(1));
        for (var i = 0; i <= 7; i++)
        {
            var date = DateTime.Today.AddDays(i);
            if (lower.Contains(date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)) dates.Add(date.Date);
        }
        if (dates.Count == 0) dates.Add(DateTime.Now.Hour >= 16 ? DateTime.Today.AddDays(1) : DateTime.Today);
        return dates.Distinct().Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).OrderBy(date => date).ToList();
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> CalculateFreeWindows(
        DateTimeOffset dayStart, DateTimeOffset dayEnd, IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> occupied)
    {
        var gaps = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var cursor = dayStart;
        foreach (var block in occupied.OrderBy(item => item.Start))
        {
            if (block.End <= cursor || block.Start >= dayEnd) continue;
            var start = block.Start < dayStart ? dayStart : block.Start;
            var end = block.End > dayEnd ? dayEnd : block.End;
            if (start > cursor) gaps.Add((cursor, start));
            if (end > cursor) cursor = end;
        }
        if (cursor < dayEnd) gaps.Add((cursor, dayEnd));
        return gaps;
    }

    private async Task<string?> TryBuildCalendarDayBriefingAsync(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var overviewPhrases = new[]
        {
            "what does", "what's", "whats", "show me", "tell me",
            "about my day", "my day for", "look like", "have i got"
        };
        var mentionsCalendar = lower.Contains("day") || lower.Contains("calendar") ||
            lower.Contains("diary") || lower.Contains("schedule") ||
            lower.Contains("today") || lower.Contains("tomorrow");
        if (!mentionsCalendar || !overviewPhrases.Any(lower.Contains))
            return null;

        var targetDate = ResolveCalendarOverviewDate(lower);
        var offset = TimeZoneInfo.Local.GetUtcOffset(targetDate);
        var dayStart = new DateTimeOffset(targetDate, offset);
        var calendar = _services.GetRequiredService<ICalendarService>();
        var events = (await calendar.GetEventsAsync(dayStart, dayStart.AddDays(1)))
            .OrderBy(item => item.Start)
            .ToList();

        var text = new System.Text.StringBuilder();
        text.AppendLine($"Here is your calendar for {targetDate:dddd dd MMMM yyyy}, directly from Microsoft 365:");
        text.AppendLine();

        if (events.Count == 0)
        {
            text.AppendLine("No calendar events are currently scheduled.");
        }
        else
        {
            foreach (var item in events)
            {
                var time = item.IsAllDay
                    ? "All day"
                    : $"{item.Start:HH:mm}–{item.End:HH:mm}";
                var kind = item.IsNekomataManaged ? "Nekomata block" : "protected calendar event";
                var location = string.IsNullOrWhiteSpace(item.Location) ? string.Empty : $" · {item.Location}";
                text.AppendLine($"• {time} · {item.Subject}{location} [{kind}]");
            }
        }

        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var workStart = new DateTimeOffset(settings.GetStart(targetDate), offset);
        var workEnd = new DateTimeOffset(settings.GetEnd(targetDate), offset);
        var occupied = events
            .Where(item => item.IsAllDay || (item.Start < workEnd && item.End > workStart))
            .Select(item => item.IsAllDay
                ? (Start: workStart, End: workEnd)
                : (Start: item.Start < workStart ? workStart : item.Start,
                   End: item.End > workEnd ? workEnd : item.End))
            .OrderBy(item => item.Start)
            .ToList();

        var gaps = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var cursor = workStart;
        foreach (var block in occupied)
        {
            if (block.End <= cursor)
                continue;
            if (block.Start > cursor)
                gaps.Add((cursor, block.Start));
            if (block.End > cursor)
                cursor = block.End;
        }
        if (cursor < workEnd)
            gaps.Add((cursor, workEnd));

        text.AppendLine();
        var usefulGaps = gaps.Where(gap => gap.End - gap.Start >= TimeSpan.FromMinutes(15)).ToList();
        text.AppendLine(usefulGaps.Count == 0
            ? "Open working windows: none."
            : "Open working windows: " + string.Join(", ", usefulGaps.Select(gap => $"{gap.Start:HH:mm}–{gap.End:HH:mm}")) + ".");

        var mission = Workspace.CurrentMission;
        if (!string.IsNullOrWhiteSpace(mission?.Title))
        {
            var alreadyScheduled = events.Any(item =>
                item.Subject.Contains(mission.Title, StringComparison.OrdinalIgnoreCase));
            text.AppendLine(alreadyScheduled
                ? $"Guardian priority: {mission.Title} already has a matching calendar block."
                : $"Guardian priority: {mission.Title} is not currently represented by a matching calendar event.");
        }

        text.AppendLine("I have not merged, moved or inferred any events in this view.");
        return text.ToString().TrimEnd();
    }

    private static DateTime ResolveCalendarOverviewDate(string lowerMessage)
    {
        if (lowerMessage.Contains("tomorrow"))
            return DateTime.Today.AddDays(1);
        if (lowerMessage.Contains("today"))
            return DateTime.Today;

        for (var daysAhead = 0; daysAhead <= 7; daysAhead++)
        {
            var candidate = DateTime.Today.AddDays(daysAhead);
            if (lowerMessage.Contains(candidate.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return DateTime.Today;
    }
    private async Task<string> AddCalendarContextAsync(string userMessage)
    {
        var calendarTerms = new[]
        {
            "calendar", "schedule", "reschedule", "plan", "diary", "move ", "slot", "slotted",
            "book ", "block ", "appointment", "meeting", "tomorrow", "today",
            "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"
        };
        if (!calendarTerms.Any(term => userMessage.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return userMessage;

        try
        {
            var calendar = _services.GetRequiredService<ICalendarService>();
            var firstDate = DateTime.Today;
            var offset = TimeZoneInfo.Local.GetUtcOffset(firstDate);
            var start = new DateTimeOffset(firstDate, offset);
            var end = start.AddDays(8);
            var events = await calendar.GetEventsAsync(start, end);
            var context = new System.Text.StringBuilder();
            context.AppendLine($"Coverage: {firstDate:dddd dd MMMM yyyy} through {firstDate.AddDays(7):dddd dd MMMM yyyy}.");

            for (var day = 0; day < 8; day++)
            {
                var date = firstDate.AddDays(day);
                var dayEvents = events
                    .Where(item => item.Start.Date == date.Date ||
                        (item.IsAllDay && item.Start.Date <= date.Date && item.End.Date > date.Date))
                    .OrderBy(item => item.Start)
                    .ToList();
                context.AppendLine();
                context.AppendLine($"DATE {date:dddd dd MMMM yyyy}");
                if (dayEvents.Count == 0)
                {
                    context.AppendLine("NO EVENTS RETURNED FOR THIS DATE");
                    continue;
                }

                foreach (var item in dayEvents)
                {
                    var people = item.Attendees.Count == 0
                        ? string.Empty
                        : $" | ATTENDEES={string.Join(", ", item.Attendees)}";
                    context.AppendLine(
                        $"EVENT ID={item.Id} | START={item.Start:O} | END={item.End:O} | " +
                        $"SUBJECT={item.Subject} | MOVABLE={item.IsNekomataManaged}" +
                        $"{people} | LOCATION={item.Location}");
                }
            }

            return $"""
                {userMessage}

                CALENDAR SNAPSHOT (authoritative)
                {context.ToString().TrimEnd()}

                Every listed event is real Microsoft 365 data. MOVABLE=false events are immovable constraints.
                Only MOVABLE=true Nekomata blocks may be moved. A covered date showing NO EVENTS is available,
                subject to working hours and any protected lunch rule; it is not unavailable.
                """;
        }
        catch (Exception ex)
        {
            return $"{userMessage}{Environment.NewLine}{Environment.NewLine}CALENDAR SNAPSHOT UNAVAILABLE: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AskGuardianAboutProject(
        NekomataProject? project)
    {
        if (project is null)
            return;

        GuardianPanelExpanded = true;

        ChatInput = $"""
            Review the project "{project.Name}".

            Please assess:
            - its current progress
            - the next action
            - any risks or missing work
            - the best next three tasks
            - whether its remaining effort looks realistic
            """;
    }

    [RelayCommand]
    private async Task GenerateProjectTasksAsync(
        NekomataProject? project)
    {
        if (project is null || GuardianBusy)
            return;

        if (!EnsurePersonalAiConfigured())
        {
            GuardianPanelExpanded = true;
            return;
        }

        GuardianBusy = true;
        GuardianPanelExpanded = true;

        try
        {
            var prompt = $$"""
                You are Guardian, the AI assistant inside Nekomata.

                Generate a practical task plan for this project.

                PROJECT
                ID: {{project.Id}}
                Name: {{project.Name}}
                Description: {{project.Description ?? "No description"}}
                Priority: {{project.Priority}}
                Status: {{project.Status}}
                Progress: {{project.ProgressPercent}}%
                Business value: {{project.EstimatedBusinessValue}}
                Remaining effort: {{project.EstimatedRemainingMinutes}} minutes
                Due date: {{project.DueAt?.ToString("yyyy-MM-dd") ?? "None"}}
                At risk: {{project.AtRisk}}
                Current next action: {{project.NextAction ?? "None"}}

                Return valid JSON only.

                Use this exact structure:

                {
                  "message": "Brief explanation of the proposed plan.",
                  "actionType": "create_tasks",
                  "projectId": {{project.Id}},
                  "tasks": [
                    {
                      "title": "Task title",
                      "description": "Clear description",
                      "priority": "Low, Normal, High or Critical",
                      "estimatedMinutes": 60,
                      "estimatedBusinessValue": 5000,
                      "requiresSql": false,
                      "requiresFocus": true,
                      "suggestedDelegate": null
                    }
                  ]
                }

                Produce between 3 and 6 tasks.
                Return no markdown and no text outside the JSON.
                """;

            var action =
                await _aiProvider
                    .AskJsonAsync<GuardianActionResponse>(
                        prompt);

            if (action is null ||
                !action.ActionType.Equals(
                    "create_tasks",
                    StringComparison.OrdinalIgnoreCase))
            {
                GuardianResponse =
                    "Guardian did not return a valid task proposal.";

                return;
            }

            PendingGuardianAction = action;
            PendingGuardianProject = project;
            GuardianProposalVisible = true;

            GuardianResponse = $"""
                {action.Message}

                Guardian prepared {action.Tasks.Count} proposed tasks
                for {project.Name}.

                Nothing has been saved yet.
                """;

            ChatHistory.Add(
                new GuardianChatTurn
                {
                    Role = "assistant",
                    Content = GuardianResponse
                });
        }
        catch (Exception ex)
        {
            GuardianResponse =
                "Guardian could not generate the task proposal: " +
                ex.Message;
        }
        finally
        {
            GuardianBusy = false;
        }
    }

    [RelayCommand]
    private void DismissGuardianProposal()
    {
        PendingGuardianAction = null;
        PendingGuardianProject = null;
        GuardianProposalVisible = false;
    }

    [RelayCommand]
    private async Task RegenerateGuardianProposalAsync()
    {
        if (PendingGuardianProject is null ||
            GuardianBusy)
        {
            return;
        }

        GuardianProposalVisible = false;

        await GenerateProjectTasksAsync(
            PendingGuardianProject);
    }

    [RelayCommand]
    private async Task CreateSelectedTasksAsync()
    {
        if (PendingGuardianAction is null || GuardianBusy)
            return;

        var selectedTasks = PendingGuardianAction.Tasks.Where(item => item.Selected).ToList();
        var selectedChanges = PendingGuardianAction.Changes.Where(item => item.Selected).ToList();
        if (selectedTasks.Count == 0 && selectedChanges.Count == 0)
        {
            GuardianResponse = "Select at least one proposed action.";
            GuardianPanelExpanded = true;
            return;
        }

        var calendarTargetDate = selectedChanges
            .Where(item => item.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.NewValue.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault())
            .Select(value => DateTimeOffset.TryParse(value, out var parsed) ? parsed.Date : (DateTime?)null)
            .FirstOrDefault(value => value.HasValue);

        GuardianBusy = true;
        try
        {
            var result = await _guardianApplyService.ApplyAsync(PendingGuardianAction, PendingGuardianProject?.Id);
            var memoryRepository = _services.GetRequiredService<IGuardianMemoryRepository>();
            await memoryRepository.AddAsync(new GuardianMemory
            {
                Category = selectedChanges.Any(item => item.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
                    ? "CalendarManaged"
                    : "TasksCreated",
                Importance = 75,
                Source = "Guardian",
                Summary = $"Applied {result.Actions.Count} Guardian action(s).",
                Detail = result.UserSummary,
                ProjectId = PendingGuardianProject?.Id
            });

            if (result.Actions.Count > 0)
                DismissGuardianProposal();

            await _workspaceCoordinator.RefreshAsync();
            if (result.Actions.Any(item => item.Type == "Calendar"))
            {
                OnPropertyChanged(nameof(CanUndoCalendarPlan));
                SelectedCalendarDate = calendarTargetDate ?? SelectedCalendarDate;
                WorkspaceMode = Nekomata.Models.Workspace.WorkspaceMode.Calendar;
                await RefreshCalendarAsync();
            }

            GuardianResponse = result.UserSummary;
            ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
            GuardianPanelExpanded = true;
        }
        catch (Exception ex)
        {
            GuardianResponse = "Guardian could not apply the selected actions: " + ex.Message;
            ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
            GuardianPanelExpanded = true;
        }
        finally
        {
            GuardianBusy = false;
        }
    }
    [RelayCommand]
    private async Task AskGuardianAboutRecommendationAsync()
    {
        if (TopRecommendation is null ||
            GuardianBusy)
        {
            return;
        }

        GuardianPanelExpanded = true;

        ChatInput = $"""
            Review Guardian's current recommendation:

            Type: {TopRecommendation.RecommendationType}
            Title: {TopRecommendation.Title}
            Score: {TopRecommendation.Score}
            Priority: {TopRecommendation.Priority}
            Business value: {TopRecommendation.BusinessValue:C0}
            Estimated effort: {TopRecommendation.EstimatedMinutes} minutes
            Due: {TopRecommendation.DueAt?.ToString("dd MMM yyyy") ?? "No due date"}
            Progress: {TopRecommendation.ProgressPercent}%
            Reason: {TopRecommendation.Reason}

            Give me:
            - the immediate first action
            - a practical sequence of steps
            - the main risks or blockers
            - how I should use the estimated time
            """;

        await SendGuardianMessageAsync();
    }

    [RelayCommand]
    private async Task StartRecommendedMissionAsync()
    {
        if (TopRecommendation is null ||
            GuardianBusy)
        {
            return;
        }

        var matchesCurrentMission =
            string.Equals(
                TopRecommendation.Title,
                Workspace.CurrentMission?.Title,
                StringComparison.OrdinalIgnoreCase);

        if (matchesCurrentMission)
        {
            await BeginMissionAsync();
            return;
        }

        GuardianPanelExpanded = true;

        ChatInput = $"""
            Turn the current recommendation into a focused mission:

            Title: {TopRecommendation.Title}
            Score: {TopRecommendation.Score}
            Priority: {TopRecommendation.Priority}
            Business value: {TopRecommendation.BusinessValue:C0}
            Estimated effort: {TopRecommendation.EstimatedMinutes} minutes
            Due: {TopRecommendation.DueAt?.ToString("dd MMM yyyy") ?? "No due date"}
            Reason: {TopRecommendation.Reason}

            Create:
            - the mission objective
            - the first action
            - three to five execution steps
            - a definition of done
            - risks or dependencies
            """;

        await SendGuardianMessageAsync();
    }

    private async Task RememberImportantConversationAsync(
        IGuardianMemoryRepository memoryRepository,
        string userMessage,
        string guardianResponse)
    {
        var lowerMessage =
            userMessage.ToLowerInvariant();

        var explicitlyRequested =
            lowerMessage.Contains("remember") ||
            lowerMessage.Contains("don't forget") ||
            lowerMessage.Contains("do not forget");

        var containsDecision =
            lowerMessage.Contains("approved") ||
            lowerMessage.Contains("agreed") ||
            lowerMessage.Contains("decision") ||
            lowerMessage.Contains("deadline") ||
            lowerMessage.Contains("must be") ||
            lowerMessage.Contains("needs to be");

        if (!explicitlyRequested &&
            !containsDecision)
        {
            return;
        }

        await memoryRepository.AddAsync(
            new GuardianMemory
            {
                Category =
                    explicitlyRequested
                        ? "UserMemory"
                        : "DecisionRecorded",

                Importance =
                    explicitlyRequested
                        ? 90
                        : 75,

                Source = "Conversation",

                Summary =
                    userMessage.Length <= 200
                        ? userMessage
                        : $"{userMessage[..197]}...",

                Detail = $"""
                    User:
                    {userMessage}

                    Guardian:
                    {guardianResponse}
                    """
            });
    }
}
