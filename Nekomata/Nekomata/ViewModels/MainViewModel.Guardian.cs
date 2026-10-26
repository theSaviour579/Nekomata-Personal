using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.UI.Controls.Common;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;
using Nekomata.Core.Analytics;
using Nekomata.Core.Commitments;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.Models.Projects;
using Nekomata.Models.Tasks;
using Nekomata.Models.Planning;
using Nekomata.Models.Missions;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using System.Collections.ObjectModel;
using System.Windows;
using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.IO;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<GuardianChatTurn>
        ChatHistory
    { get; } = [];

    public ObservableCollection<GuardianAttachmentItem> PendingChatAttachments { get; } = [];

    public bool HasPendingChatAttachments => PendingChatAttachments.Count > 0;

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
    private async Task AddGuardianAttachmentsAsync()
    {
        var picker = new OpenFileDialog
        {
            Title = "Attach evidence to Guardian",
            Multiselect = true,
            Filter = "Supported files|*.pdf;*.docx;*.xlsx;*.csv;*.tsv;*.txt;*.sql;*.json;*.xml;*.md;*.log;*.png;*.jpg;*.jpeg|All files|*.*"
        };
        if (picker.ShowDialog() != true) return;
        var importer = _services.GetRequiredService<GuardianAttachmentService>();
        foreach (var path in picker.FileNames)
        {
            try
            {
                var imported = await importer.ImportAsync(path);
                PendingChatAttachments.Add(new GuardianAttachmentItem(imported));
            }
            catch (Exception ex)
            {
                GuardianResponse = $"Could not attach {Path.GetFileName(path)}: {ex.Message}";
            }
        }
        OnPropertyChanged(nameof(HasPendingChatAttachments));
    }

    [RelayCommand]
    private void RemoveGuardianAttachment(GuardianAttachmentItem? item)
    {
        if (item is null || !PendingChatAttachments.Remove(item)) return;
        try { File.Delete(item.Attachment.StoredPath); } catch { /* retained safely if in use */ }
        OnPropertyChanged(nameof(HasPendingChatAttachments));
    }

    private bool _routingChatCommand;

    /// <summary>Intents this method answers itself (or that only make sense as a Guardian conversation).</summary>
    private static bool HandledByGuardianChat(GuardianCommandIntent intent) => intent is
        GuardianCommandIntent.Unknown or GuardianCommandIntent.ShowWeeklyOvertime or GuardianCommandIntent.ShowExecutiveValue or
        GuardianCommandIntent.ShowStopDoing or GuardianCommandIntent.ShowWorkDebt or
        GuardianCommandIntent.ShowIOwe or GuardianCommandIntent.ShowOwedToMe or GuardianCommandIntent.ShowAtRiskCommitments or
        GuardianCommandIntent.ShowWaitingCommitments or GuardianCommandIntent.ShowPromisedThisWeek or GuardianCommandIntent.ShowCallCommitments or
        GuardianCommandIntent.ShowHealth or GuardianCommandIntent.ShowUnhealthyIntegrations or GuardianCommandIntent.ShowLastSync or
        GuardianCommandIntent.ShowRoleAlignment or GuardianCommandIntent.ProtectMyRole or GuardianCommandIntent.ForecastRoleAlignment or
        GuardianCommandIntent.ImproveRoleAlignment or GuardianCommandIntent.ExplainRoleDrift or GuardianCommandIntent.ShowMostImportantCorrection or
        GuardianCommandIntent.ProtectStrategicTime or GuardianCommandIntent.ExplainRoleBias or
        GuardianCommandIntent.FindTime or GuardianCommandIntent.PlanTomorrow;

    [RelayCommand]
    private async Task SendGuardianMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput) ||
            GuardianBusy)
        {
            return;
        }
        MarkUserEngaged();

        // The Ask box takes the same commands as the palette and the voice ("who fixed the VPN last time?", "what did I
        // miss?", "reply to this"): a recognised command runs locally, and its answer lands in this conversation.
        if (!_routingChatCommand && PendingChatAttachments.Count == 0 && !GuardianBusy)
        {
            var typed = ChatInput.Trim();
            var intent = GuardianCommandParser.Parse(typed).Intent;
            GuardianCommandMatch? routed = null;
            if (intent == GuardianCommandIntent.Unknown && PendingChatAttachments.Count == 0 &&
                !string.Equals(_aiRoutedText, typed, StringComparison.Ordinal))
            {
                GuardianBusy = true;
                try { routed = await RouteWithAiAsync(typed); }
                finally { GuardianBusy = false; }
            }
            _aiRoutedText = null;
            if (routed is not null || !HandledByGuardianChat(intent))
            {
                _preRouted = routed;
                ChatInput = "";
                ChatHistory.Add(new GuardianChatTurn { Role = "user", Content = typed });
                _routingChatCommand = true;
                try
                {
                    CommandPaletteInput = typed;
                    await ExecuteCommandPaletteAsync();
                }
                finally { _routingChatCommand = false; }
                return;
            }
        }

        var userMessage =
            ChatInput.Trim();
        Think(userMessage.Length > 70 ? $"Thinking about: {userMessage[..70]}…" : $"Thinking about: {userMessage}");

        var attachments = PendingChatAttachments.Select(item => item.Attachment).ToList();
        var attachmentNames = attachments.Select(item => item.FileName).ToList();

        ChatInput = "";
        GuardianBusy = true;
        GuardianPresenceState = GuardianPresenceState.Thinking;
        GuardianPanelExpanded = true;

        if (!_routingChatCommand) // a command that fell back to Guardian already shows what the user typed
            ChatHistory.Add(
                new GuardianChatTurn
                {
                    Role = "user",
                    Content = userMessage,
                    AttachmentNames = attachmentNames
                });

        PendingChatAttachments.Clear();
        OnPropertyChanged(nameof(HasPendingChatAttachments));

        try
        {
            var localIntent = GuardianCommandParser.Parse(userMessage).Intent;
            if (localIntent == GuardianCommandIntent.ShowWeeklyOvertime)
            {
                GuardianResponse = FormatOvertimeSummary(await GetWeeklyOvertimeAsync());
                PendingGuardianAction = null;
                GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                _ = SpeakGuardianAsync(GuardianResponse);
                return;
            }
            if (localIntent == GuardianCommandIntent.ShowExecutiveValue)
            {
                GuardianResponse = FormatExecutiveValueSummary(await GetCurrentWeekExecutiveValueAsync());
                PendingGuardianAction = null;
                GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                _ = SpeakGuardianAsync(GuardianResponse);
                return;
            }
            if (localIntent == GuardianCommandIntent.ShowStopDoing)
            {
                GuardianResponse = FormatStopDoing(await GetCurrentWeekExecutiveValueAsync());
                PendingGuardianAction = null;
                GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                _ = SpeakGuardianAsync(GuardianResponse);
                return;
            }
            if (localIntent == GuardianCommandIntent.ShowWorkDebt)
            {
                await RefreshWorkDebtAsync();
                GuardianResponse = WorkDebt.Items.Count == 0 ? "No repeated operational pattern currently has enough evidence to qualify as Work Debt." :
                    $"Work Debt · {WorkDebt.TotalMinutes} observed minutes · {WorkDebt.OvertimeMinutes} outside working hours\n" +
                    string.Join("\n", WorkDebt.Items.Take(8).Select(x => $"• {x.Status} · {x.Title} — {x.TotalTimeMinutes}m across {x.EpisodeCount} episodes · {x.Confidence}% confidence\n  {x.Recommendation}"));
                PendingGuardianAction = null; GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES"; _ = SpeakGuardianAsync(GuardianResponse); return;
            }
            if (localIntent is GuardianCommandIntent.ShowIOwe or GuardianCommandIntent.ShowOwedToMe or
                GuardianCommandIntent.ShowAtRiskCommitments or GuardianCommandIntent.ShowWaitingCommitments or
                GuardianCommandIntent.ShowPromisedThisWeek or GuardianCommandIntent.ShowCallCommitments)
            {
                await RefreshCommitmentSummaryAsync();
                IEnumerable<Commitment> selected = localIntent switch
                {
                    GuardianCommandIntent.ShowIOwe => ActiveCommitments.Where(x => x.Direction == CommitmentDirection.OwedByMe),
                    GuardianCommandIntent.ShowOwedToMe => ActiveCommitments.Where(x => x.Direction == CommitmentDirection.OwedToMe),
                    GuardianCommandIntent.ShowAtRiskCommitments => ActiveCommitments.Where(x => x.Status is CommitmentStatus.DueSoon or CommitmentStatus.Overdue),
                    GuardianCommandIntent.ShowWaitingCommitments => ActiveCommitments.Where(x => x.Status == CommitmentStatus.WaitingExternal),
                    GuardianCommandIntent.ShowPromisedThisWeek => ActiveCommitments.Where(x => x.Direction == CommitmentDirection.OwedByMe && x.CreatedAt >= DateTimeOffset.Now.AddDays(-7)),
                    _ => ActiveCommitments.Where(x => x.SourceType.Contains("call", StringComparison.OrdinalIgnoreCase) || x.SourceType.Contains("goto", StringComparison.OrdinalIgnoreCase))
                };
                var commitments = selected.Take(20).ToList();
                GuardianResponse = commitments.Count == 0 ? "No matching active commitments were found." : string.Join("\n", commitments.Select(x =>
                    $"• {x.Status} · {x.Summary} · {(x.DueAt is null ? "no explicit due date" : x.DueAt.Value.LocalDateTime.ToString("ddd dd MMM HH:mm"))} · {x.Confidence}% confidence\n  Evidence: {x.EvidenceSummary}"));
                PendingGuardianAction = null; GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES"; _ = SpeakGuardianAsync(GuardianResponse); return;
            }
            if (localIntent is GuardianCommandIntent.ShowRoleAlignment or GuardianCommandIntent.ProtectMyRole or
                GuardianCommandIntent.ForecastRoleAlignment or GuardianCommandIntent.ImproveRoleAlignment or
                GuardianCommandIntent.ExplainRoleDrift or GuardianCommandIntent.ShowMostImportantCorrection or
                GuardianCommandIntent.ProtectStrategicTime or GuardianCommandIntent.ExplainRoleBias)
            {
                await RefreshRolePlanningAsync();
                GuardianResponse = localIntent switch
                {
                    GuardianCommandIntent.ShowRoleAlignment => FormatRoleAlignment(RoleAlignment!),
                    GuardianCommandIntent.ProtectMyRole => FormatRoleProtection(RoleAlignment!),
                    _ => FormatRolePlanningCommand(localIntent)
                };
                PendingGuardianAction = null;
                GuardianProposalVisible = false;
                ChatHistory.Add(new GuardianChatTurn { Role = "assistant", Content = GuardianResponse });
                GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
                _ = SpeakGuardianAsync(GuardianResponse);
                return;
            }

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
                    UserMessage = AddSituationContext(await AddJournalContextAsync(await AddCalendarContextAsync(
                        AddAttachmentContext(userMessage, attachments)), userMessage)),
                    IntentMessage = userMessage,
                    Workspace = Workspace,
                    CurrentTime = DateTime.Now,
                    Conversation = ChatHistory.ToList()
                };
                response = await _guardianConversationService.AskAsync(request);
            }

            GuardianResponse = response.Message;

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
                    Content = GuardianResponse
                });

            GuardianPanelTitle =
                $"GUARDIAN · {ChatHistory.Count} MESSAGES";

            _ = SpeakGuardianAsync(GuardianResponse);

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
            if (GuardianPresenceState == GuardianPresenceState.Thinking)
                GuardianPresenceState = GuardianPresenceState.Idle;
        }
    }

    private static string AddAttachmentContext(string message, IReadOnlyList<Nekomata.Models.Guardian.GuardianFileAttachment> attachments)
    {
        if (attachments.Count == 0) return message;
        var context = new System.Text.StringBuilder(message).AppendLine().AppendLine("Attached evidence:");
        foreach (var attachment in attachments)
        {
            context.AppendLine($"- {attachment.FileName} ({attachment.ContentType}, {attachment.SizeBytes} bytes, SHA-256 {attachment.Sha256})");
            if (!string.IsNullOrWhiteSpace(attachment.ExtractedText))
                context.AppendLine("--- extracted content ---").AppendLine(attachment.ExtractedText).AppendLine("--- end content ---");
            else
                context.AppendLine("[Stored locally; content extraction for this file type is not enabled yet.]");
        }
        return context.ToString();
    }

    private async Task<GuardianActionResponse?> TryBuildDeterministicCalendarPlanAsync(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var asksToPlan = new[] { "plan", "schedule", "fill", "organise", "organize", "slot", "slotted" }.Any(lower.Contains);
        var calendarScope = new[] { "day", "calendar", "today", "tomorrow", "monday", "tuesday", "wednesday", "thursday", "friday", "when", "next week" }.Any(lower.Contains) ||
                            ResolveMentionedCalendarDates(lower).Count > 0;
        if (!asksToPlan || !calendarScope) return null;

        var dates = ResolvePlanningDates(lower);
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var calendar = _services.GetRequiredService<ICalendarService>();
        var ticketMatch = System.Text.RegularExpressions.Regex.Match(
            userMessage,
            @"\btask\s*#?\s*(\d+)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var requestedTaskId = ticketMatch.Success && long.TryParse(ticketMatch.Groups[1].Value, out var parsedTaskId)
            ? parsedTaskId
            : (long?)null;
        var candidates = Workspace.RankedMissionCandidates
            .Where(item => item.EstimatedMinutes > 0)
            .Where(item => requestedTaskId is null || item.TaskId == requestedTaskId)
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
        var dates = ResolveMentionedCalendarDates(lower).ToList();
        if (lower.Contains("today")) dates.Add(DateTime.Today);
        if (lower.Contains("tomorrow")) dates.Add(DateTime.Today.AddDays(1));
        if (lower.Contains("next week"))
        {
            var nextMonday = DateTime.Today.AddDays(((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7);
            if (nextMonday <= DateTime.Today) nextMonday = nextMonday.AddDays(7);
            dates.AddRange(Enumerable.Range(0, 5).Select(day => nextMonday.AddDays(day)));
        }
        for (var i = 0; i <= 60; i++)
        {
            var date = DateTime.Today.AddDays(i);
            var dayName = date.DayOfWeek.ToString();
            if (!lower.Contains(dayName, StringComparison.OrdinalIgnoreCase)) continue;
            if (lower.Contains($"next {dayName.ToLowerInvariant()}") && i < 7) continue;
            dates.Add(date.Date);
            break;
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
        var explicitDate = ResolveMentionedCalendarDates(lowerMessage).FirstOrDefault();
        if (explicitDate != default) return explicitDate;
        if (lowerMessage.Contains("tomorrow"))
            return DateTime.Today.AddDays(1);
        if (lowerMessage.Contains("today"))
            return DateTime.Today;

        for (var daysAhead = 0; daysAhead <= 60; daysAhead++)
        {
            var candidate = DateTime.Today.AddDays(daysAhead);
            var dayName = candidate.DayOfWeek.ToString();
            if (lowerMessage.Contains(dayName, StringComparison.OrdinalIgnoreCase) &&
                (!lowerMessage.Contains($"next {dayName.ToLowerInvariant()}") || daysAhead >= 7))
                return candidate;
        }

        return DateTime.Today;
    }

    private static IReadOnlyList<DateTime> ResolveMentionedCalendarDates(string text)
    {
        var dates = new List<DateTime>();
        foreach (Match match in Regex.Matches(text, @"\b(?<date>\d{4}-\d{2}-\d{2})\b"))
            if (DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
                dates.Add(iso.Date);

        foreach (Match match in Regex.Matches(text,
                     @"\b(?<day>\d{1,2})(?:st|nd|rd|th)?\s+(?<month>jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)(?:\s+(?<year>\d{4}))?\b",
                     RegexOptions.IgnoreCase))
        {
            var year = match.Groups["year"].Success ? match.Groups["year"].Value : DateTime.Today.Year.ToString(CultureInfo.InvariantCulture);
            if (!DateTime.TryParse($"{match.Groups["day"].Value} {match.Groups["month"].Value} {year}", CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out var parsed)) continue;
            if (!match.Groups["year"].Success && parsed.Date < DateTime.Today) parsed = parsed.AddYears(1);
            dates.Add(parsed.Date);
        }
        return dates.Distinct().OrderBy(date => date).ToList();
    }
    private async Task<string> AddCalendarContextAsync(string userMessage)
    {
        var calendarTerms = new[]
        {
            "calendar", "schedule", "reschedule", "plan", "diary", "move ", "slot", "slotted",
            "book ", "block ", "appointment", "meeting", "tomorrow", "today",
            "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"
        };
        if (!calendarTerms.Any(term => userMessage.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
            ResolveMentionedCalendarDates(userMessage).Count == 0)
            return userMessage;

        try
        {
            var calendar = _services.GetRequiredService<ICalendarService>();
            var mentionedDates = ResolveMentionedCalendarDates(userMessage);
            var planningDates = ResolvePlanningDates(userMessage.ToLowerInvariant());
            var requestedDates = mentionedDates.Count > 0 ? mentionedDates : planningDates;
            var firstDate = requestedDates.Count > 0 ? requestedDates.Min().Date : DateTime.Today;
            var lastDate = requestedDates.Count > 0 ? requestedDates.Max().Date : firstDate.AddDays(7);
            if (lastDate < firstDate.AddDays(7) && mentionedDates.Count == 0) lastDate = firstDate.AddDays(7);
            if ((lastDate - firstDate).TotalDays > 31) lastDate = firstDate.AddDays(31);
            var offset = TimeZoneInfo.Local.GetUtcOffset(firstDate);
            var start = new DateTimeOffset(firstDate, offset);
            var end = new DateTimeOffset(lastDate.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(lastDate.AddDays(1)));
            var events = await calendar.GetEventsAsync(start, end);
            var context = new System.Text.StringBuilder();
            context.AppendLine($"Coverage: {firstDate:dddd dd MMMM yyyy} through {lastDate:dddd dd MMMM yyyy}.");

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                var dayStart = new DateTimeOffset(date.Date, TimeZoneInfo.Local.GetUtcOffset(date.Date));
                var dayEnd = new DateTimeOffset(date.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(date.Date.AddDays(1)));
                var dayEvents = events
                    .Where(item => item.Start < dayEnd && item.End > dayStart)
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
            var proposalMemories = await _services.GetRequiredService<IGuardianMemoryRepository>().GetRecentAsync(50);
            var proposalGuidance = _services.GetRequiredService<Nekomata.Core.Guardian.Learning.GuardianProposalLearningPolicy>()
                .BuildGuidance(proposalMemories, project.Id);
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

                REVIEWED PLANNING PREFERENCES
                {{proposalGuidance}}

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
            var feedbackCount = await PersistProposalFeedbackAsync();
            if (feedbackCount > 0)
            {
                DismissGuardianProposal();
                GuardianResponse = $"Saved {feedbackCount} planning preference{(feedbackCount == 1 ? "" : "s")}. Guardian will use this evidence in future proposals.";
            }
            else
                GuardianResponse = "Select at least one proposed action or remove an item with feedback.";
            GuardianPanelExpanded = true;
            return;
        }

        var calendarTargetDate = selectedChanges
            .Where(item => item.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.NewValue.Split('|', StringSplitOptions.TrimEntries).FirstOrDefault())
            .Select(value => DateTimeOffset.TryParse(value, out var parsed) ? parsed.Date : (DateTime?)null)
            .FirstOrDefault(value => value.HasValue);

        GuardianBusy = true;
        var missionWasActive = MissionActive && _activeMission is not null;
        var isDecisionSimulation = PendingGuardianAction.ActionType.Equals("decision_simulation_plan", StringComparison.OrdinalIgnoreCase);
        var decisionSimulationNarrative = PendingGuardianAction.Message;
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
            var proposalFeedbackCount = await PersistProposalFeedbackAsync();

            if (isDecisionSimulation && result.Actions.Count > 0)
            {
                var chain = await RecordDecisionChainAsync(decisionSimulationNarrative, "Accepted");
                await RecordWorkdayEventAsync(new WorkdayEvent
                {
                    CorrelationId = chain.CorrelationId, ParentEventId = chain.DecisionEventId,
                    EventType = "DecisionScenarioApplied", Category = "Planning", Title = "Simulated plan accepted",
                    Narrative = $"Applied {result.Actions.Count} reviewed calendar block(s). {decisionSimulationNarrative}",
                    Recommendation = decisionSimulationNarrative, UserDecision = "Accepted", Outcome = "Applied",
                    Significance = 80
                });
            }

            if (result.Actions.Count > 0)
                DismissGuardianProposal();

            await _workspaceCoordinator.RefreshAsync();
            if (result.Actions.Any(item => item.Type == "Calendar"))
            {
                OnPropertyChanged(nameof(CanUndoCalendarPlan));
                SelectedCalendarDate = calendarTargetDate ?? SelectedCalendarDate;
                WorkspaceMode = ActiveMissionFocusPolicy.ResolveModeAfterCalendarAction(missionWasActive);
                await RefreshCalendarAsync();
                if (missionWasActive)
                    ReturnToActiveMission();
            }

            GuardianResponse = result.UserSummary;
            if (proposalFeedbackCount > 0)
                GuardianResponse += $"{Environment.NewLine}Guardian learned from {proposalFeedbackCount} removed or replaced suggestion{(proposalFeedbackCount == 1 ? "" : "s")}.";
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
        if (MissionActive)
        {
            await BeginMissionAsync();
            return;
        }

        if (TopRecommendation is null ||
            GuardianBusy)
        {
            return;
        }

        await RecordRecommendationDecisionAsync($"Start {TopRecommendation.Title}", "Accepted", metadata: BuildRecommendationEvidence(TopRecommendation));
        var candidate = Workspace.RankedMissionCandidates.FirstOrDefault(item =>
                            TopRecommendation.TaskId is long taskId && item.TaskId == taskId)
                        ?? Workspace.RankedMissionCandidates.FirstOrDefault(item =>
                            TopRecommendation.ProjectId is long projectId && item.ProjectId == projectId &&
                            item.Title.Equals(TopRecommendation.Title, StringComparison.OrdinalIgnoreCase))
                        ?? new MissionCandidate
                        {
                            SourceType = TopRecommendation.RecommendationType,
                            TaskId = TopRecommendation.TaskId,
                            ProjectId = TopRecommendation.ProjectId,
                            Title = TopRecommendation.Title,
                            Description = TopRecommendation.Reason,
                            EstimatedMinutes = Math.Max(1, TopRecommendation.EstimatedMinutes),
                            BusinessValue = TopRecommendation.BusinessValue,
                            Priority = TopRecommendation.Priority,
                            DueAt = TopRecommendation.DueAt,
                            Score = TopRecommendation.Score,
                            AtRisk = TopRecommendation.AtRisk,
                            Progress = TopRecommendation.ProgressPercent
                        };
        Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(candidate);
        OnPropertyChanged(nameof(Workspace));
        await BeginMissionAsync();
    }

    private static string BuildRecommendationEvidence(GuardianDashboardRecommendation recommendation) =>
        JsonSerializer.Serialize(new
        {
            recommendation.Score,
            recommendation.Priority,
            recommendation.EstimatedMinutes,
            recommendation.DueAt,
            recommendation.BusinessValue,
            recommendation.AtRisk,
            recommendation.Reason
        });

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
