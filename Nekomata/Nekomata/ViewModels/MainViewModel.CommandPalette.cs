using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Commands;
using Nekomata.Core.Missions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Workspace;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool commandPaletteVisible;
    [ObservableProperty] private bool commandPaletteCompact;
    [ObservableProperty] private bool speechInputActive;
    [ObservableProperty] private string commandPaletteInput = "";
    [ObservableProperty] private string commandPaletteStatus = "Type a command or search an objective, project or person.";
    public ObservableCollection<GuardianCommandPaletteItem> CommandPaletteSuggestions { get; } = [];

    [RelayCommand]
    private void OpenCommandPalette()
    {
        CommandPaletteCompact = false;
        CommandPaletteInput = "";
        CommandPaletteStatus = "Deterministic commands run locally. Guardian interprets only when the intent is unclear.";
        CommandPaletteVisible = true;
        _ = RefreshCommandPaletteSuggestionsAsync();
    }

    [RelayCommand]
    private void OpenQuickCapture()
    {
        CommandPaletteCompact = true;
        CommandPaletteInput = "";
        CommandPaletteStatus = "Capture, command or fuzzy-search everything · Enter to run · microphone for dictation";
        CommandPaletteVisible = true;
        _ = RefreshCommandPaletteSuggestionsAsync();
    }

    [RelayCommand] private void CloseCommandPalette() => CommandPaletteVisible = false;

    partial void OnCommandPaletteInputChanged(string value) => _ = RefreshCommandPaletteSuggestionsAsync();

    private int _paletteSearchGeneration;
    private async Task RefreshCommandPaletteSuggestionsAsync()
    {
        var generation = ++_paletteSearchGeneration;
        CommandPaletteSuggestions.Clear();
        var query = CommandPaletteInput.Trim();
        if (query.Length == 0)
        {
            AddSuggestion("brief me", "Speak the current objective, meetings, attention and capacity");
            AddSuggestion("what can you do with this?", "What Guardian can do with the window you're in, or ask about Teams, calendar or while you're away");
            AddSuggestion("review my invites", "Unanswered invitations with what to reply; do it sends the reply");
            AddSuggestion("what's this?", "Guardian looks at the window you were using: reply to this, make this a task, who is this");
            AddSuggestion("when did we last …", "Ask the history: email, Teams, chronicle and notes, with sources");
            AddSuggestion("what did I miss?", "One catch-up since you last looked: managers, people waiting, email");
            AddSuggestion("catch me up on teams", "Managers, people waiting on you and what's changed in your chats");
            AddSuggestion("what needs attention?", "Open Guardian Attention");
            AddSuggestion("find me the next thing to do", "Promote the strongest actionable objective unless the calendar currently takes priority");
            AddSuggestion("log interruption", "Triage an interruption without surrendering your priority");
            AddSuggestion("plan tomorrow", "Open tomorrow planning");
            AddSuggestion("email my status", "Send today's Guardian status to your signed-in mailbox");
            AddSuggestion("weekly report", "Export this week's Guardian evidence as Markdown");
            AddSuggestion("overtime summary", "Show recorded additional time outside your working hours and lunch");
            AddSuggestion("executive value", "Connect invested time, overtime, value evidence and strategic relevance");
            AddSuggestion("what should I stop doing?", "Challenge repeated low-leverage work using recorded evidence");
            AddSuggestion("role alignment", "Compare this week's work with your role profile");
            AddSuggestion("protect my role", "Identify work Guardian should challenge, queue or delegate");
            AddSuggestion("where will my role alignment end this week?", "Compare observed alignment with both deterministic forecasts");
            AddSuggestion("how do I improve my role alignment?", "Show Guardian's ranked minimum viable correction plan");
            AddSuggestion("what is pulling me out of role?", "Explain the observed drift and its likely causes");
            AddSuggestion("which corrective action matters most?", "Show the highest-benefit intervention and evidence");
            AddSuggestion("email weekly report", "Send this week's Guardian report to yourself");
            AddSuggestion("product friction", "Rank recurring workflow friction after 10 working days of evidence");
            AddSuggestion("undo last guardian action", "Conflict-aware undo of the latest reversible action");
            return;
        }

        foreach (var candidate in PaletteMissionCandidates()
                     .Select(item => (Item: item, Score: FuzzyScore(item.Title, query)))
                     .Where(item => item.Score > 0).OrderByDescending(item => item.Score).Take(4).Select(item => item.Item))
            AddSuggestion($"start {candidate.Title}", candidate.Rank > 0
                ? $"Start objective · {candidate.EstimatedMinutes}m · score {candidate.Score}"
                : $"Start connected work · {candidate.EstimatedMinutes}m · {candidate.SourceType}");
        foreach (var project in Workspace.Projects.Select(item => (Item: item, Score: FuzzyScore(item.Name, query)))
                     .Where(item => item.Score > 0).OrderByDescending(item => item.Score).Take(3).Select(item => item.Item))
            AddSuggestion($"show project {project.Name}", $"Project · {project.Status} · {project.ProgressPercent}% complete");
        foreach (var email in EmailItems.Select(item => (Item: item, Score: Math.Max(FuzzyScore(item.Message.SenderName, query), FuzzyScore(item.Message.Subject, query))))
                     .Where(item => item.Score > 0).OrderByDescending(item => item.Score).Take(3).Select(item => item.Item))
            AddSuggestion($"emails from {email.Message.SenderName}", email.Message.Subject);

        if (query.Length >= 2)
        {
            var journal = await _services.GetRequiredService<IWorkdayEventRepository>().SearchAsync(query, 8);
            if (generation != _paletteSearchGeneration) return;
            foreach (var entry in journal.OrderByDescending(item => FuzzyScore($"{item.Title} {item.Narrative}", query)).Take(3))
                AddSuggestion($"history {query}", $"Chronicle · {entry.OccurredAt:ddd HH:mm} · {entry.Title} — {entry.Narrative}");
        }

        var parsed = GuardianCommandParser.Parse(query);
        if (parsed.Intent != GuardianCommandIntent.Unknown)
            AddSuggestion(query, $"Run {parsed.Intent.ToString().Replace("Show", "Show ").Replace("Log", "Log ")}");
        else
            AddSuggestion(query, "Ask Guardian — no unambiguous local command found");
    }

    private void AddSuggestion(string command, string description)
    {
        if (!CommandPaletteSuggestions.Any(x => x.Command.Equals(command, StringComparison.OrdinalIgnoreCase)))
            CommandPaletteSuggestions.Add(new(command, description));
    }

    [RelayCommand]
    private async Task RunPaletteSuggestionAsync(GuardianCommandPaletteItem? item)
    {
        if (item is null) return;
        CommandPaletteInput = item.Command;
        await ExecuteCommandPaletteAsync();
    }

    [RelayCommand]
    private async Task ExecuteCommandPaletteAsync()
    {
        var input = CommandPaletteInput.Trim();
        if (input.Length == 0) return;
        MarkUserEngaged();
        if (_promiseReview is not null && await TryHandlePromiseReviewInputAsync(input))
        {
            CommandPaletteVisible = false;
            return;
        }
        var match = _preRouted ?? GuardianCommandParser.Parse(input);
        _preRouted = null;
        CommandPaletteVisible = false;
        if (match.Intent == GuardianCommandIntent.Unknown && !_routingChatCommand)
        {
            _aiRoutedText = input;
            if (await RouteWithAiAsync(input) is { } routed) match = routed;
        }
        RecordCapabilityUse(match.Intent);

        switch (match.Intent)
        {
            case GuardianCommandIntent.StartMission: await StartPaletteMissionAsync(match.Argument); return;
            case GuardianCommandIntent.ShowAttention: ShowAttention(); return;
            case GuardianCommandIntent.ExplainLastSurfaced: ExplainLastSurfacedEntity(); return;
            case GuardianCommandIntent.LogInterruption:
                OpenInterruption();
                if (!string.IsNullOrWhiteSpace(match.Argument)) { InterruptionInput = match.Argument; await TriageInterruptionAsync(); }
                return;
            case GuardianCommandIntent.ShowProject: ShowPaletteProject(match.Argument); return;
            case GuardianCommandIntent.PlanTomorrow:
                WorkspaceMode = WorkspaceMode.Calendar;
                ChatInput = "Plan tomorrow using my strongest evidence-backed focus window and predicted calendar requirements.";
                GuardianPanelExpanded = true;
                // Typed, this leaves the prompt ready to edit; spoken, there's no box to press send in.
                if (_conversationActive) await SendGuardianMessageAsync();
                return;
            case GuardianCommandIntent.EmailsFrom: await ShowPaletteEmailsAsync(match.Argument); return;
            case GuardianCommandIntent.FindTime:
                ChatInput = $"Find me {match.Minutes} minutes tomorrow for focused work."; await SendGuardianMessageAsync(); return;
            case GuardianCommandIntent.BeforeLunch: await ShowBeforeLunchAsync(); return;
            case GuardianCommandIntent.UndoLastAction: await UndoLastGuardianActionAsync(); return;
            case GuardianCommandIntent.ExplainDecision: await ExplainGuardianDecisionAsync(match.Argument); return;
            case GuardianCommandIntent.ExplainRecommendation: await ExplainRecommendationAsync(match.Argument, input); return;
            case GuardianCommandIntent.OpenEntity: await OpenResolvedEntityAsync(match.Argument, input); return;
            case GuardianCommandIntent.ScheduleEntity: await ScheduleResolvedEntityAsync(match.Argument, input); return;
            case GuardianCommandIntent.BriefMe: await BriefMeAsync(); return;
            case GuardianCommandIntent.NoteForMeeting: await NoteForMeetingAsync(match.Argument); return;
            case GuardianCommandIntent.QuietMode: ApplyQuietModeCommand(match.Argument, match.Minutes); return;
            case GuardianCommandIntent.WrapUpDay: await WrapUpDayOnRequestAsync(); return;
            case GuardianCommandIntent.InterruptionFeedback: ApplyInterruptionFeedback(match.Argument); return;
            case GuardianCommandIntent.RememberFact: await RememberFactAsync(match.Argument); return;
            case GuardianCommandIntent.ForgetFact: await ForgetFactAsync(match.Argument); return;
            case GuardianCommandIntent.ListFacts: await ListFactsAsync(); return;
            case GuardianCommandIntent.DraftEmailReply: await DraftVoiceReplyAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.PersonBrief: await PersonBriefAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.PromisesToday: await PromisesTodayAsync(); return;
            case GuardianCommandIntent.StartCurrentBlock: await StartCurrentBlockAsync(); return;
            case GuardianCommandIntent.PushCurrentBlock: await PushCurrentBlockAsync(); return;
            case GuardianCommandIntent.AutopilotToggle: await SetAutopilotAsync(match.Argument == "on"); return;
            case GuardianCommandIntent.PlanStatus: await ShowPlanStatusAsync(); return;
            case GuardianCommandIntent.ShowNeglect: await ShowNeglectAsync(); return;
            case GuardianCommandIntent.NeglectCorrection: await ApplyNeglectCorrectionAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.Handoff: await BuildHandoffAsync(speak: true); return;
            case GuardianCommandIntent.TeamsCatchUp: await TeamsCatchUpAsync(); return;
            case GuardianCommandIntent.WhatDidIMiss: await WhatDidIMissAsync(); return;
            case GuardianCommandIntent.ScreenThis: await ScreenThisAsync(match.Argument); return;
            case GuardianCommandIntent.AskHistory: await AskHistoryAsync(match.Argument); return;
            case GuardianCommandIntent.OpenHistorySource: await OpenHistorySourceAsync(match.Minutes); return;
            case GuardianCommandIntent.ReviewInvites: await ReviewAllInvitesAsync(); return;
            case GuardianCommandIntent.Capabilities: await ShowCapabilitiesAsync(match.Argument); return;
            case GuardianCommandIntent.SelfCheck: await SelfCheckAsync(onDemand: true); return;
            case GuardianCommandIntent.ApprovalInbox: await ShowApprovalInboxAsync(); return;
            case GuardianCommandIntent.Weather: await SayWeatherAsync(); return;
            case GuardianCommandIntent.Correction: await ApplyCorrectionAsync(match.Argument); return;
            case GuardianCommandIntent.FeedbackSummary: await ShowFeedbackAsync(); return;
            case GuardianCommandIntent.JobHealth: await JobHealthReportAsync(); return;
            case GuardianCommandIntent.Scorecard: await ScorecardAsync(match.Argument); return;
            case GuardianCommandIntent.AiPrivacyReport: await AiPrivacyReportAsync(); return;
            case GuardianCommandIntent.AnythingToWorry: await AnythingToWorryAboutAsync(); return;
            case GuardianCommandIntent.RunProtocol: await RunProtocolAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.AddWatch: await AddWatchAsync(input); return;
            case GuardianCommandIntent.Music: await MusicAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.ListWatches: ListWatches(); return;
            case GuardianCommandIntent.CancelWatch: await CancelWatchAsync(match.Argument); return;
            case GuardianCommandIntent.ListProtocols: ListProtocols(); return;
            case GuardianCommandIntent.CreateProtocol: await CreateProtocolAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.DeleteProtocol: await DeleteProtocolAsync(match.Argument); return;
            case GuardianCommandIntent.FormalManner:
                Prefs.Features["formal-voice"] = match.Argument == "on";
                SaveJson(GuardianPreferencesPath, Prefs);
                Nekomata.Core.Guardian.FormalManner.Enabled = match.Argument == "on";
                await RespondAndSpeakAsync(match.Argument == "on"
                    ? "Very good, Sir."
                    : $"Of course{(UserFirstName.Length > 0 ? $", {UserFirstName}" : "")}. I'll keep it less formal.");
                return;
            case GuardianCommandIntent.MissedIssue: await RecordMissedAsync(match.Argument); return;
            case GuardianCommandIntent.WeekReview: await WeekReviewAsync(announce: true); return;
            case GuardianCommandIntent.SetupCheck:
                if (match.Argument.Length > 0) await SetupDoneAsync(match.Argument); else await SetupCheckAsync();
                return;
            case GuardianCommandIntent.MorningBrief: MarkUserEngaged(); await SpeakMorningBriefAsync(); return;
            case GuardianCommandIntent.Planner: OpenPlanner("duties"); ShowPaletteResponse("The Planner is open."); return;
            case GuardianCommandIntent.WhatsDue: await WhatsDueAsync(); return;
            case GuardianCommandIntent.DutyDone: await DutyDoneAsync(match.Argument); return;
            case GuardianCommandIntent.AddObjective: await AddObjectiveAsync(match.Argument); return;
            case GuardianCommandIntent.Objectives: await ShowObjectivesAsync(); return;
            case GuardianCommandIntent.ObjectiveEvidence: await ExportObjectiveEvidenceAsync(); return;
            case GuardianCommandIntent.LinkObjectives: Think("Linking recent work to your objectives"); await LinkObjectivesAsync(announce: true); return;
            case GuardianCommandIntent.TimeToday: await WhereDidMyTimeGoAsync(week: false); return;
            case GuardianCommandIntent.TimeWeek: await WhereDidMyTimeGoAsync(week: true); return;
            case GuardianCommandIntent.MeetingReview: await MeetingReviewAsync(); return;
            case GuardianCommandIntent.MeetingLoad: await MeetingLoadNowAsync(); return;
            case GuardianCommandIntent.Place: await PlaceCommandAsync(match.Argument); return;
            case GuardianCommandIntent.GuardianSettings: OpenGuardianSettings(); ShowPaletteResponse("Settings are open."); return;
            case GuardianCommandIntent.ApproveIt: await DecideApprovalAsync(true, match.Argument); return;
            case GuardianCommandIntent.DeclineIt: await DecideApprovalAsync(false, match.Argument); return;
            case GuardianCommandIntent.ProjectStatus: await ProjectStatusAsync(match.Argument); return;
            case GuardianCommandIntent.ProjectUpdate: await DraftProjectUpdateAsync(announce: true); return;
            case GuardianCommandIntent.TeamsFrom: await TeamsFromAsync(match.Argument); return;
            case GuardianCommandIntent.TeamsManager: await TeamsManagerAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.BargeInToggle: await SetBargeInAsync(match.Argument == "on"); return;
            case GuardianCommandIntent.PlanMyDay: await PlanMyDayAsync(); return;
            case GuardianCommandIntent.MeetingFollowUp: await FollowThroughMeetingsAsync(onDemand: true); return;
            case GuardianCommandIntent.EnableMeetingFollowThrough: await EnableMeetingFollowThroughAsync(); return;
            case GuardianCommandIntent.DelegateTask: await DelegateAsync(match.Argument, match.Detail); return;
            case GuardianCommandIntent.ShowDelegations: await ShowDelegationsAsync(); return;
            case GuardianCommandIntent.ShowLearned: await ShowLearnedProfileAsync(); return;
            case GuardianCommandIntent.ResetLearning: await ResetLearningAsync(); return;
            case GuardianCommandIntent.ShowPreparedReplies: await ShowPreparedRepliesAsync(); return;
            case GuardianCommandIntent.ConfirmPending: await ConfirmPendingAutonomyAsync(input); return;
            case GuardianCommandIntent.AutonomySettings: await DescribeAutonomyAsync(); return;
            case GuardianCommandIntent.ReadImportantEmails: await ReadImportantEmailsAsync(); return;
            case GuardianCommandIntent.ToggleHud:
                SetHudVisible(match.Argument == "show");
                ShowPaletteResponse(match.Argument == "show" ? "Heads-up is on. Ctrl+Alt+H toggles it." : "Heads-up is off.");
                return;
            case GuardianCommandIntent.ShowGuardianChanges: await ShowGuardianChangesAsync(); return;
            case GuardianCommandIntent.ShowApprovals: await ShowApprovalsAsync(); return;
            case GuardianCommandIntent.ShowAnticipatedActions: ShowAnticipatedActions(); return;
            case GuardianCommandIntent.ApproveAnticipatedActions: await ApproveAnticipatedActionsAsync(); return;
            case GuardianCommandIntent.SendAnticipatedActions: await SendAnticipatedActionsAsync(); return;
            case GuardianCommandIntent.CancelDraftAndCloseLoop: await CancelDraftAndCloseLoopAsync(match.Argument); return;
            case GuardianCommandIntent.PrepareNextMeeting: ShowNextMeetingPreparation(); return;
              case GuardianCommandIntent.ShowThreadNextActions: ShowThreadNextActions(match.Argument); return;
              case GuardianCommandIntent.ShowScheduledInterventions: ShowScheduledInterventions(match.Argument); return;
              case GuardianCommandIntent.ShowPredictionCalibration: ShowPredictionCalibration(match.Argument); return;
            case GuardianCommandIntent.SearchHistory: await ShowJournalSearchAsync(match.Argument); return;
            case GuardianCommandIntent.EmailStatusToSelf: await SendAwayStatusToSelfAsync(manual: true); return;
            case GuardianCommandIntent.WeeklyReport: await ExportWeeklyReportAsync(); return;
            case GuardianCommandIntent.EmailWeeklyReport: await EmailWeeklyReportAsync(); return;
            case GuardianCommandIntent.ShowWeeklyOvertime: await ShowWeeklyOvertimeAsync(); return;
            case GuardianCommandIntent.ShowExecutiveValue: await ShowExecutiveValueAsync(); return;
            case GuardianCommandIntent.ShowStopDoing: await ShowStopDoingAsync(); return;
            case GuardianCommandIntent.ShowWorkDebt: await ShowWorkDebtCommandAsync(); return;
            case GuardianCommandIntent.ShowIOwe: await StartPromiseReviewAsync(); return;
            case GuardianCommandIntent.ShowOwedToMe:
            case GuardianCommandIntent.ShowAtRiskCommitments:
            case GuardianCommandIntent.ShowWaitingCommitments:
            case GuardianCommandIntent.ShowPromisedThisWeek:
            case GuardianCommandIntent.ShowCallCommitments:
                await ShowCommitmentCommandAsync(match.Intent); return;
            case GuardianCommandIntent.ShowRoleAlignment: await ShowRoleAlignmentAsync(); return;
            case GuardianCommandIntent.ProtectMyRole: await ProtectMyRoleAsync(); return;
            case GuardianCommandIntent.ForecastRoleAlignment:
            case GuardianCommandIntent.ImproveRoleAlignment:
            case GuardianCommandIntent.ExplainRoleDrift:
            case GuardianCommandIntent.ShowMostImportantCorrection:
            case GuardianCommandIntent.ProtectStrategicTime:
            case GuardianCommandIntent.ExplainRoleBias:
                await ShowRolePlanningCommandAsync(match.Intent); return;
            case GuardianCommandIntent.SearchEmail: await ShowRemoteEmailSearchAsync(match.Argument); return;
            case GuardianCommandIntent.ProductFriction: OpenProductFriction(); return;
            case GuardianCommandIntent.FindNextThing: await FindAndPromoteNextObjectiveAsync(); return;
            default:
                ChatInput = input; await SendGuardianMessageAsync(); return;
        }
    }

    private async Task ShowJournalSearchAsync(string query)
    {
        var entries = await _services.GetRequiredService<IWorkdayEventRepository>().SearchAsync(query, 20);
        ShowPaletteResponse(entries.Count == 0
            ? $"No Chronicle entries matched '{query}'."
            : $"Chronicle results for '{query}':\n" + string.Join("\n", entries.Take(10).Select(item => $"• {item.OccurredAt:ddd dd MMM HH:mm} · {item.Title} — {item.Narrative}")));
    }

    private async Task ShowGuardianChangesAsync()
    {
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.LocalDateTime.Date, now.Offset);
        var entries = (await _services.GetRequiredService<IWorkdayEventRepository>()
                .GetBetweenAsync(start, start.AddDays(1), 500))
            .Where(Nekomata.Core.Guardian.Briefing.GuardianExecutiveBriefingService.IsCompletedAdministrativeAction)
            .GroupBy(x => x.EventId).Select(x => x.First()).OrderBy(x => x.OccurredAt).ToList();
        var response = entries.Count == 0
            ? "Guardian has reconciled today's workspace, but has not completed any autonomous administrative changes today. Authority evaluations and observed external changes are not counted as actions."
            : $"Guardian completed {entries.Count} administrative action{(entries.Count == 1 ? "" : "s")} today:\n" +
              string.Join("\n", entries.Select(x => $"• {x.OccurredAt:HH:mm} · {x.Title} — {x.Narrative}"));
        ShowPaletteResponse(response);
        await SpeakGuardianAsync(response, true);
    }

    private async Task ShowApprovalsAsync()
    {
        var prepared = await _services.GetRequiredService<IGuardianChaseRepository>()
            .GetByStatusAsync(Nekomata.Models.Guardian.GuardianChaseStatus.Prepared);
        var approvedChases = await _services.GetRequiredService<IGuardianChaseRepository>()
            .GetByStatusAsync(Nekomata.Models.Guardian.GuardianChaseStatus.Approved);
        var now = DateTimeOffset.Now;
        var authority = (await _services.GetRequiredService<IWorkdayEventRepository>()
                .GetBetweenAsync(now.AddDays(-1), now.AddMinutes(1), 500))
            .Where(x => x.Outcome.Equals("ApprovalRequired", StringComparison.OrdinalIgnoreCase) ||
                        x.EventType.Contains("ApprovalRequired", StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.CorrelationId).Select(x => x.OrderByDescending(y => y.OccurredAt).First()).ToList();

        var anticipated = Workspace.AnticipatedActions.Where(x => x.Status is Nekomata.Models.Guardian.AnticipatedActionStatus.Prepared or
                Nekomata.Models.Guardian.AnticipatedActionStatus.Approved &&
            !string.IsNullOrWhiteSpace(x.PreparedContent) && x.ExpiresAt > now).ToList();
        var lines = prepared.Select(x =>
                $"• AWAITING APPROVAL · Follow-up attempt {x.AttemptNumber} to {x.Recipient.DefaultIfBlank("unresolved recipient")} — {x.Subject.DefaultIfBlank(x.ContextLabel).DefaultIfBlank("untitled follow-up")}\n  Draft:\n{x.Body}\n  Prepared {x.PreparedAt.LocalDateTime:ddd HH:mm}; nothing has been sent.")
            .Concat(approvedChases.Select(x =>
                $"• APPROVED · Follow-up attempt {x.AttemptNumber} to {x.Recipient.DefaultIfBlank("unresolved recipient")} — {x.Subject.DefaultIfBlank(x.ContextLabel).DefaultIfBlank("untitled follow-up")}\n  Draft:\n{x.Body}\n  Approved {(x.ApprovedAt?.LocalDateTime.ToString("ddd HH:mm") ?? "time unavailable")}; awaiting send."))
            .Concat(anticipated.Select(x => $"• {x.Status.ToString().ToUpperInvariant()} · Teams response — {x.Reason}\n  Draft:\n{x.PreparedContent}\n  Nothing has been sent."))
            .Concat(authority.Select(x => $"• {x.Title} — {x.Narrative}"))
            .ToList();
        var response = lines.Count == 0
            ? "There are no prepared or approved drafts in the action queue."
            : $"Guardian action queue ({lines.Count} item{(lines.Count == 1 ? "" : "s")}):\n" +
              string.Join("\n", lines) +
              (prepared.Count > 0 ? "\n\nUse ‘approve follow-ups’ to approve chase drafts." : "") +
              (approvedChases.Count > 0 ? "\nUse ‘send approved follow-ups’ to send the approved chase drafts." : "") +
              (anticipated.Count > 0 ? "\nUse ‘approve prepared Teams responses’ to approve Teams drafts. Approval will not send them." : "");
        ShowPaletteResponse(response);
        await SpeakGuardianAsync(response, true);
    }

    private async Task ApproveAnticipatedActionsAsync()
    {
        var approved = await _services.GetRequiredService<Nekomata.Core.Guardian.Anticipation.AnticipatedActionWorkflowService>()
            .ApprovePreparedAsync(Workspace, DateTimeOffset.Now);
        ShowPaletteResponse(approved.Count == 0 ? "There are no prepared Teams responses awaiting approval." :
            $"Approved {approved.Count} prepared Teams response{(approved.Count == 1 ? "" : "s")}. Nothing has been sent. Use ‘send approved Teams responses’ when ready.");
    }

    private async Task SendAnticipatedActionsAsync()
    {
        var results = await _services.GetRequiredService<Nekomata.Core.Guardian.Anticipation.AnticipatedActionWorkflowService>()
            .SendApprovedAsync(Workspace, DateTimeOffset.Now);
        ShowPaletteResponse(results.Count == 0 ? "There are no approved Teams responses ready to send." :
            string.Join("\n", results.Select(x => $"• {x.Action.ProposedAction}: {x.Message}")));
    }

    private async Task CancelDraftAndCloseLoopAsync(string query)
    {
        var result = await _services.GetRequiredService<Nekomata.Core.Guardian.OpenLoops.GuardianLoopClosureService>()
            .CancelDraftAndResolveAsync(query, DateTimeOffset.Now);
        ShowPaletteResponse(result.Message);
    }

    private void ShowAnticipatedActions()
    {
        var now = DateTimeOffset.Now;
        var items = Workspace.AnticipatedActions.Where(x => x.ExpiresAt > now &&
                x.Status is not Nekomata.Models.Guardian.AnticipatedActionStatus.Completed and
                    not Nekomata.Models.Guardian.AnticipatedActionStatus.Cancelled and
                    not Nekomata.Models.Guardian.AnticipatedActionStatus.Expired)
            .OrderBy(x => x.ExecuteAfter ?? now).ToList();
        ShowPaletteResponse(items.Count == 0
            ? "Guardian has no active anticipatory actions queued."
            : $"Guardian is anticipating {items.Count} action{(items.Count == 1 ? "" : "s")}:\n" +
              string.Join("\n", items.Select(x =>
                  $"• {x.ProposedAction} · {x.Status} · {x.Authority} · {x.Confidence}%\n  {x.Reason}\n  " +
                  $"Act after: {(x.ExecuteAfter is null ? "now" : x.ExecuteAfter.Value.LocalDateTime.ToString("ddd HH:mm"))} · expires {x.ExpiresAt.LocalDateTime:ddd HH:mm}" +
                  (string.IsNullOrWhiteSpace(x.PreparedContent) ? "" : $"\n  Prepared draft:\n{x.PreparedContent}"))));
    }

    private void ShowNextMeetingPreparation()
    {
        var now = DateTimeOffset.Now;
        var action = Workspace.AnticipatedActions.Where(x => x.TriggerType == "UpcomingMeeting" &&
                x.ExpiresAt > now && !string.IsNullOrWhiteSpace(x.PreparedContent))
            .OrderBy(x => x.ExpiresAt).FirstOrDefault();
        ShowPaletteResponse(action?.PreparedContent ??
            "Guardian has no prepared context for a confirmed upcoming meeting. Refresh Calendar to reconcile current commitments.");
    }

    private void ShowThreadNextActions(string query)
    {
        var items = Workspace.WorkThreadNextActions.AsEnumerable();
        if (query.Contains("move forward", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Disposition == Nekomata.Models.Guardian.WorkThreadNextActionDisposition.ActNow);
        else if (query.Contains("correctly waiting", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.Disposition == Nekomata.Models.Guardian.WorkThreadNextActionDisposition.Wait);
        else
        {
            var marker = query.IndexOf("next for ", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                var scope = query[(marker + 9)..].Trim().TrimEnd('?', '.', '!');
                items = items.Where(x => x.ThreadTitle.Contains(scope, StringComparison.OrdinalIgnoreCase) ||
                    Workspace.WorkThreads.Any(t => t.Id == x.WorkThreadId && t.PrimaryProjectId is long projectId &&
                        Workspace.Projects.Any(p => p.Id == projectId && p.Name.Contains(scope, StringComparison.OrdinalIgnoreCase))));
            }
        }
        var result = items.OrderBy(x => x.Disposition).ThenByDescending(x => x.Confidence).ToList();
        ShowPaletteResponse(result.Count == 0 ? "Guardian has no matching deterministic Work Thread next action." :
            string.Join("\n", result.Select(x =>
                $"• {x.ThreadTitle} — {x.Disposition}: {x.Title}\n  {x.Reason}\n  Evidence: {string.Join("; ", x.Evidence)}" +
                (x.ReconsiderAt is DateTimeOffset at ? $"\n  Reconsider: {at.LocalDateTime:ddd dd MMM HH:mm}" : ""))));
    }

    private void ShowScheduledInterventions(string query)
    {
        var now = DateTimeOffset.Now;
        var items = Workspace.ScheduledInterventions.Where(x =>
                x.Status is not Nekomata.Models.Guardian.ScheduledInterventionStatus.Completed and
                    not Nekomata.Models.Guardian.ScheduledInterventionStatus.Cancelled and
                    not Nekomata.Models.Guardian.ScheduledInterventionStatus.Superseded && x.ExpiresAt > now)
            .AsEnumerable();
        if (query.Contains("reconsidering today", StringComparison.OrdinalIgnoreCase))
            items = items.Where(x => x.ActionType == "WaitForDependency" && x.TriggerAt.LocalDateTime.Date == DateTime.Today);
        else if (query.Contains("handle next", StringComparison.OrdinalIgnoreCase) ||
                 query.Contains("why", StringComparison.OrdinalIgnoreCase))
            items = items.OrderBy(x => x.TriggerAt).Take(1);
        var result = items.OrderBy(x => x.TriggerAt).ThenBy(x => x.Title).ToList();
        ShowPaletteResponse(result.Count == 0 ? "Guardian has no matching scheduled intervention." :
            string.Join("\n", result.Select(x =>
                $"• {x.Title} — {x.Status} — {x.TriggerAt.LocalDateTime:ddd dd MMM HH:mm}\n" +
                $"  {x.Reason}\n  Authority: {x.Authority} · Evidence: {string.Join("; ", x.Evidence)}" +
                (string.IsNullOrWhiteSpace(x.PreparedContent) ? "" : "\n  Preparation is already available."))));
    }

    private void ShowPredictionCalibration(string query)
    {
        var value = Workspace.PredictionCalibration;
        if (query.Contains("wrong", StringComparison.OrdinalIgnoreCase))
        {
            var exceptions = value.Outcomes.Where(x => x.Disposition is
                Nekomata.Models.Guardian.PredictionOutcomeDisposition.Overridden or
                Nekomata.Models.Guardian.PredictionOutcomeDisposition.Premature or
                Nekomata.Models.Guardian.PredictionOutcomeDisposition.Late or
                Nekomata.Models.Guardian.PredictionOutcomeDisposition.Missed).ToList();
            ShowPaletteResponse(exceptions.Count == 0 ? "Guardian has no measured prediction errors yet." :
                "Measured prediction exceptions:\n" + string.Join("\n", exceptions.Select(x =>
                    $"• {x.Title} — {x.Disposition}: {x.Reason}")));
            return;
        }
        if (query.Contains("admin time", StringComparison.OrdinalIgnoreCase))
        {
            ShowPaletteResponse($"Guardian has conservatively attributed {value.EstimatedAdminMinutesSaved} minutes of administrative work saved to completed interventions.");
            return;
        }
        ShowPaletteResponse(value.TotalPredictions == 0 ? "Guardian has no prediction outcomes to calibrate yet." :
            $"Prediction calibration\n• {value.TotalPredictions} predictions · {value.EvaluatedPredictions} measured · {value.PendingPredictions} pending\n" +
            $"• Acceptance: {value.AcceptancePercent}% · timing accuracy: {value.TimingAccuracyPercent}% · false positives: {value.FalsePositivePercent}%\n" +
            $"• Accepted {value.AcceptedPredictions}; overridden {value.OverriddenPredictions}; superseded {value.SupersededPredictions}; premature {value.PrematurePredictions}; late {value.LatePredictions}; missed {value.MissedPredictions}\n" +
            $"• Estimated administrative time saved: {value.EstimatedAdminMinutesSaved} minutes");
    }

    private async Task StartPaletteMissionAsync(string query)
    {
        var resolution = ResolveEntity(query, GuardianEntityType.Task);
        if (!resolution.IsResolved || resolution.Entity is null)
        {
            await FallBackToGuardianAsync($"start {query}");
            return;
        }

        switch (resolution.Entity.Value)
        {
            case Nekomata.Models.Missions.MissionCandidate candidate:
                if (MissionActive)
                {
                    GuardianResponse = $"Finish, hold or discard the active mission before starting '{candidate.Title}'.";
                    GuardianPanelExpanded = true;
                    return;
                }
                Workspace.CurrentMission = _services.GetRequiredService<IMissionFactory>().Create(candidate);
                OnPropertyChanged(nameof(Workspace));
                await BeginMissionAsync();
                return;
            case Nekomata.Models.Tasks.NekomataTask task:
                await StartTaskMissionAsync(task);
                return;
            default:
                await FallBackToGuardianAsync($"start {query}");
                return;
        }
    }

    private void ShowPaletteProject(string query)
    {
        var project = ResolveEntity(query, GuardianEntityType.Project).Entity?.Value as Nekomata.Models.Projects.NekomataProject;
        if (project is null) { ShowPaletteResponse($"I couldn't find a project matching '{query}'."); return; }
        WorkspaceMode = WorkspaceMode.Dashboard;
        ShowPaletteResponse($"{project.Name}\n{project.Status} · {project.ProgressPercent}% complete · {project.EstimatedRemainingMinutes}m remaining\nNext: {project.NextAction ?? "No next action recorded."}");
    }

    private async Task ShowPaletteEmailsAsync(string sender)
    {
        await ShowEmailAsync();
        var matches = EmailItems.Where(x => Contains(x.Message.SenderName, sender) || Contains(x.Message.SenderAddress, sender)).Take(8).ToList();
        ShowPaletteResponse(matches.Count == 0 ? $"No loaded unread email from {sender}." :
            $"Unread from {sender}:\n" + string.Join("\n", matches.Select(x => $"• {x.Message.Subject}")));
    }

    private async Task ShowBeforeLunchAsync()
    {
        var start = new DateTimeOffset(DateTime.Today, TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        var lunch = start.AddHours(13);
        var events = await _services.GetRequiredService<IWorkdayEventRepository>().GetBetweenAsync(start, lunch, 100);
        var meaningful = events.OrderByDescending(x => x.OccurredAt).FirstOrDefault(x => x.Category is "Mission" or "Planning" or "Interruption");
        ShowPaletteResponse(meaningful is null ? "There is no meaningful Chronicle event before lunch today." :
            $"Before lunch, the latest recorded working state was:\n{meaningful.TimeLabel} — {meaningful.Title}\n{meaningful.Narrative}");
    }

    private async Task UndoLastGuardianActionAsync()
    {
        var latest = (await _services.GetRequiredService<IGuardianAuditRepository>().GetRecentAsync(100))
            .FirstOrDefault(x => x.CanUndo);
        if (latest is null) { ShowPaletteResponse("There is no reversible Guardian action to undo."); return; }
        var result = await _services.GetRequiredService<GuardianUndoService>().UndoBatchAsync(latest.BatchId);
        if (result.Success) { OnPropertyChanged(nameof(Workspace)); NoteAutopilotUndo(latest.BatchId); }
        ShowPaletteResponse(result.Message);
    }

    private async Task ExplainGuardianDecisionAsync(string query)
    {
        var events = await _services.GetRequiredService<IWorkdayEventRepository>().SearchAsync(query, 100);
        var decision = events.FirstOrDefault(x => x.EventType.Contains("Decision", StringComparison.OrdinalIgnoreCase) ||
                                                  x.Recommendation.Length > 0 || x.UserDecision.Length > 0);
        if (decision is null) { ShowPaletteResponse($"The Chronicle has no decision provenance matching '{query}'."); return; }
        var chain = events.Where(x => x.CorrelationId == decision.CorrelationId).OrderBy(x => x.OccurredAt).ToList();
        var explanation = $"Why Guardian acted\n{decision.Recommendation.DefaultIfBlank(decision.Narrative)}\n\nDecision lineage · {decision.CorrelationLabel}\n" +
            string.Join("\n", chain.Select(x => $"{x.TimeLabel} — {x.Title}: {x.UserDecision.DefaultIfBlank(x.Outcome).DefaultIfBlank(x.Narrative)}"));
        ShowPaletteResponse(explanation);
        TrackSurfacedEntity("Explanation", decision.CorrelationId.ToString(), decision.Title, "ExplainDecision", explanation);
    }

    private async Task BriefMeAsync()
    {
        var briefing = await _services
            .GetRequiredService<Nekomata.Core.Guardian.Briefing.GuardianExecutiveBriefingService>()
            .BuildAsync(Workspace, CalendarEvents.ToList(), DateTimeOffset.Now);
        ShowPaletteResponse(briefing.Narrative);
        await SpeakGuardianAsync(briefing.Narrative, true);
    }

    private async Task OpenResolvedEntityAsync(string query, string original)
    {
        var resolution = ResolveEntity(query);
        if (!resolution.IsResolved) { await FallBackToGuardianAsync(original); return; }
        switch (resolution.Entity!.Type)
        {
            case GuardianEntityType.Task:
                var candidate = resolution.Entity.Value as Nekomata.Models.Missions.MissionCandidate;
                ShowPaletteResponse(candidate is null ? $"{resolution.Entity.Name} · task #{resolution.Entity.Id}" :
                    $"{candidate.Title}\nTask #{resolution.Entity.Id} · score {candidate.Score} · {candidate.EstimatedMinutes}m\n{candidate.RecommendationReason}");
                break;
            case GuardianEntityType.Project: ShowPaletteProject(resolution.Entity.Name); break;
            case GuardianEntityType.Person: await ShowPaletteEmailsAsync(resolution.Entity.Name); break;
        }
    }

    private async Task ScheduleResolvedEntityAsync(string query, string original)
    {
        var resolution = ResolveEntity(query);
        if (!resolution.IsResolved || resolution.Entity!.Type is GuardianEntityType.Person)
        { await FallBackToGuardianAsync(original); return; }
        var name = resolution.Entity.Name;
        ChatInput = $"Schedule {name} tomorrow using its predicted calendar requirement.";
        await SendGuardianMessageAsync();
    }

    private async Task ExplainRecommendationAsync(string query, string original)
    {
        var resolution = ResolveEntity(query, GuardianEntityType.Task);
        var candidate = resolution.Entity?.Value as Nekomata.Models.Missions.MissionCandidate;
        if (!resolution.IsResolved || candidate is null) { await FallBackToGuardianAsync(original); return; }
        var factors = candidate.ScoreFactors.OrderByDescending(x => Math.Abs(x.Points)).Take(5)
            .Select(x => $"• {x.Name}: {x.Points:+#;-#;0}");
        ShowPaletteResponse($"Why {candidate.Title} is recommended\nScore {candidate.Score} · rank {candidate.Rank}\n{candidate.RecommendationReason}\n" + string.Join("\n", factors));
        TrackSurfacedEntity("MissionCandidate", Nekomata.Core.Missions.NextActionPolicy.Key(candidate), candidate.Title, "ExplainRecommendation");
    }

    private GuardianEntityResolution ResolveEntity(string query, GuardianEntityType? preferred = null) =>
        GuardianEntityResolver.Resolve(query, BuildPaletteEntities(), preferred);

    private IEnumerable<GuardianEntity> BuildPaletteEntities()
    {
        foreach (var candidate in PaletteMissionCandidates())
        {
            var id = candidate.TaskId?.ToString() ?? candidate.SourceRecordId ?? candidate.Title;
            yield return new(GuardianEntityType.Task, id, candidate.Title, candidate);
        }
        foreach (var task in Workspace.Tasks.Where(x => PaletteMissionCandidates().All(c => c.TaskId != x.Id)))
            yield return new(GuardianEntityType.Task, task.Id.ToString(), task.Title, task);
        foreach (var project in Workspace.Projects)
            yield return new(GuardianEntityType.Project, project.Id.ToString(), project.Name, project);
        foreach (var email in EmailItems.DistinctBy(x => x.Message.SenderAddress))
            yield return new(GuardianEntityType.Person, email.Message.SenderAddress, email.Message.SenderName, email);
    }

    private IEnumerable<Nekomata.Models.Missions.MissionCandidate> PaletteMissionCandidates() =>
        Workspace.RankedMissionCandidates
            .Concat(Workspace.IntegrationMissionCandidates.Where(candidate => candidate.IsActionable))
            .DistinctBy(Nekomata.Core.Missions.NextActionPolicy.Key);

    private async Task FallBackToGuardianAsync(string input)
    {
        ChatInput = input;
        await SendGuardianMessageAsync();
    }

    private void ShowPaletteResponse(string message)
    {
        message = Nekomata.Core.Guardian.GeneratedTextDeduplicator.RemoveRepeatedBlocks(message);
        GuardianResponse = message;
        PendingGuardianAction = null;
        PendingGuardianProject = null;
        GuardianProposalVisible = false;
        ChatHistory.Add(new Nekomata.AI.Models.GuardianChatTurn
        {
            Role = "assistant",
            Content = message
        });
        GuardianPanelTitle = $"GUARDIAN · {ChatHistory.Count} MESSAGES";
        GuardianPanelExpanded = true;
    }

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static int FuzzyScore(string? value, string query)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(query)) return 0;
        var candidate = value.ToLowerInvariant();
        var needle = query.ToLowerInvariant();
        if (candidate.Contains(needle, StringComparison.Ordinal)) return 1000 - candidate.IndexOf(needle, StringComparison.Ordinal) * 2 - candidate.Length;
        var position = 0;
        var gaps = 0;
        foreach (var character in needle)
        {
            var found = candidate.IndexOf(character, position);
            if (found < 0) return 0;
            gaps += found - position;
            position = found + 1;
        }
        return Math.Max(1, 500 - gaps * 8 - candidate.Length);
    }

    private async Task ShowRemoteEmailSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { ShowPaletteResponse("Enter an email search phrase."); return; }
        try
        {
            var matches = await _services.GetRequiredService<Nekomata.Integrations.MicrosoftGraph.Mail.IEmailService>()
                .SearchMessagesAsync(query, 20);
            ShowPaletteResponse(matches.Count == 0
                ? $"No mailbox messages matched '{query}'."
                : $"Mailbox results for '{query}':\n" + string.Join("\n", matches.Select(x => $"• {x.ReceivedAt:dd MMM} · {x.SenderName} — {x.Subject}")));
        }
        catch (Exception ex) { ShowPaletteResponse($"Guardian could not search the mailbox: {ex.Message}"); }
    }
}

file static class CommandPaletteStringExtensions
{
    public static string DefaultIfBlank(this string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
