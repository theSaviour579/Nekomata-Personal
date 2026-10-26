using System.Text.RegularExpressions;

namespace Nekomata.Core.Guardian.Commands;

public enum GuardianCommandIntent
{
    Unknown, StartMission, ShowAttention, ExplainLastSurfaced, LogInterruption, ShowProject,
    PlanTomorrow, EmailsFrom, FindTime, BeforeLunch,
    UndoLastAction, ExplainDecision, ExplainRecommendation, OpenEntity, ScheduleEntity, BriefMe,
    SearchHistory, EmailStatusToSelf, WeeklyReport, EmailWeeklyReport, SearchEmail, ProductFriction, FindNextThing,
    ShowGuardianChanges, ShowApprovals, ShowAnticipatedActions, ApproveAnticipatedActions, SendAnticipatedActions,
    CancelDraftAndCloseLoop, PrepareNextMeeting, ShowThreadNextActions, ShowScheduledInterventions,
    ShowPredictionCalibration, ShowWeeklyOvertime, ShowExecutiveValue, ShowStopDoing, ShowRoleAlignment, ProtectMyRole,
    ForecastRoleAlignment, ImproveRoleAlignment, ExplainRoleDrift, ShowMostImportantCorrection,
    ProtectStrategicTime, ExplainRoleBias, ShowWorkDebt, ShowIOwe, ShowOwedToMe, ShowAtRiskCommitments,
    ShowWaitingCommitments, ShowPromisedThisWeek, ShowCallCommitments, ShowHealth, ShowUnhealthyIntegrations, ShowLastSync,
    NoteForMeeting, ToggleHud, QuietMode, WrapUpDay, InterruptionFeedback, RememberFact, ForgetFact, ListFacts, DraftEmailReply, PersonBrief, PromisesToday, ReadImportantEmails, StartCurrentBlock, PushCurrentBlock, AutopilotToggle, PlanStatus, ShowNeglect, PlanMyDay, ConfirmPending, AutonomySettings, ShowPreparedReplies, ShowLearned, ResetLearning, DelegateTask, ShowDelegations, MeetingFollowUp, EnableMeetingFollowThrough,
    NeglectCorrection, BargeInToggle, Handoff, TeamsCatchUp, TeamsFrom, TeamsManager, WhatDidIMiss, ScreenThis, AskHistory, OpenHistorySource,
    ReviewInvites, Capabilities,
    SelfCheck, ApprovalInbox, ApproveIt, DeclineIt, ProjectStatus, ProjectUpdate, GuardianSettings, Weather, Correction, FeedbackSummary, WhatsDue, DutyDone, Objectives, AddObjective, ObjectiveEvidence, LinkObjectives, TimeToday, TimeWeek, MeetingReview, MeetingLoad,
    Place, Planner, MorningBrief,
    SetupCheck, WeekReview, JobHealth, Scorecard, MissedIssue, AiPrivacyReport, AnythingToWorry, FormalManner, RunProtocol, ListProtocols, CreateProtocol, DeleteProtocol, AddWatch, ListWatches, CancelWatch, Music
}

public sealed record GuardianCommandMatch(GuardianCommandIntent Intent, string Argument = "", int Minutes = 0, string Detail = "");

public static partial class GuardianCommandParser
{
    public static GuardianCommandMatch Parse(string? input)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0) return new(GuardianCommandIntent.Unknown);
        var quiet = ParseQuietMode(text.TrimEnd('?', '!', '.'));
        if (quiet is not null) return quiet;
        var note = MeetingNoteRegex().Match(text);
        if (note.Success && note.Groups[1].Value.Trim().Length > 0)
            return new(GuardianCommandIntent.NoteForMeeting, note.Groups[1].Value.Trim());
        var exact = text.TrimEnd('?', '!', '.').Replace('\u2019', '\'');
        if (Matches(exact, "brief me", "briefing")) return new(GuardianCommandIntent.BriefMe);
        if (Matches(exact, "that wasn't important", "that was not important", "that's not important", "that is not important",
                "not important", "don't bother me with those", "don't bother me with that", "don't bother me with these",
                "less of those", "less of that", "fewer of those", "stop telling me about those", "stop telling me about that",
                "i don't care about those", "i don't need those"))
            return new(GuardianCommandIntent.InterruptionFeedback, "less");
        if (Matches(exact, "that was important", "that's important", "that is important", "always tell me about those",
                "always tell me about that", "more of those", "more of that", "i want to know about those", "keep telling me about those"))
            return new(GuardianCommandIntent.InterruptionFeedback, "more");
        if (Matches(exact, "hand off", "handoff", "hand-off", "give me the hand off", "give me the handoff", "end of day summary",
                "what does tomorrow look like", "what's tomorrow look like", "what's tomorrow like", "what have i got tomorrow", "set me up for tomorrow"))
            return new(GuardianCommandIntent.Handoff);
        var planner = ParsePlanner(exact, text.Trim());
        if (planner is not null) return planner;
        var teams = ParseTeams(exact);
        if (teams is not null) return teams;
        var correction = ParseNeglectCorrection(exact);
        if (correction is not null) return correction;
        if (Matches(exact, "let me interrupt you", "you can let me interrupt", "listen while you talk", "listen while you're talking",
                "turn on voice interrupt", "turn voice interrupt on", "barge in on"))
            return new(GuardianCommandIntent.BargeInToggle, "on");
        if (Matches(exact, "don't let me interrupt you", "stop listening while you talk", "stop listening while you're talking",
                "turn off voice interrupt", "turn voice interrupt off", "barge in off", "don't listen while you talk"))
            return new(GuardianCommandIntent.BargeInToggle, "off");
        if (Matches(exact, "reset interruption preferences", "reset my interruption preferences", "forget my interruption preferences",
                "reset what you've learned about interruptions"))
            return new(GuardianCommandIntent.InterruptionFeedback, "reset");
        if (Matches(exact, "what have i asked you to remember", "what do you remember", "what have you remembered",
                "what do you remember about me", "list what you remember", "show what you remember"))
            return new(GuardianCommandIntent.ListFacts);
        var remember = RememberRegex().Match(exact);
        if (remember.Success) return new(GuardianCommandIntent.RememberFact, remember.Groups[1].Value.Trim());
        var forget = ForgetRegex().Match(exact);
        if (forget.Success && forget.Groups[1].Value.Trim().Length > 3)
            return new(GuardianCommandIntent.ForgetFact, forget.Groups[1].Value.Trim());
        var handoff = DelegateRegex().Match(text.TrimEnd('.', '!', '?'));
        if (handoff.Success)
            return new(GuardianCommandIntent.DelegateTask, handoff.Groups["who"].Value.Trim(), 0,
                handoff.Groups["what"].Value.Trim() + "|" + handoff.Groups["when"].Value.Trim());
        var reply = ReplyRegex().Match(text.TrimEnd('.', '!', '?'));
        if (reply.Success)
            return new(GuardianCommandIntent.DraftEmailReply, reply.Groups["who"].Value.Trim(), 0, reply.Groups["what"].Value.Trim());
        if (Matches(exact, "start it", "start the block", "start this block", "start my block", "i'll start it", "start it now",
                "ok start it", "okay start it", "yes start it", "let's start it", "i'm on it"))
            return new(GuardianCommandIntent.StartCurrentBlock);
        if (Matches(exact, "push it", "push it back", "push it later", "push the block", "push this block", "push my block",
                "move this block", "move the block", "move it later", "not now, push it", "push it out"))
            return new(GuardianCommandIntent.PushCurrentBlock);
        if (Matches(exact, "autopilot on", "turn autopilot on", "turn on autopilot", "enable autopilot", "you can rearrange my calendar",
                "manage my calendar", "keep me on schedule"))
            return new(GuardianCommandIntent.AutopilotToggle, "on");
        if (Matches(exact, "autopilot off", "turn autopilot off", "turn off autopilot", "disable autopilot",
                "stop rearranging my calendar", "stop moving my blocks", "leave my calendar alone"))
            return new(GuardianCommandIntent.AutopilotToggle, "off");
        // Music (Spotify): play / pause / next / previous / shuffle / volume / what's playing / play <something>.
        if (ParseMusic(exact) is { } music) return music;
        // Watches: "tell me when Sam replies", "let me know when Kev messages me".
        if (Matches(exact, "what are you watching", "what are you keeping an eye on", "list watches", "show watches", "watches",
                "what are you watching for me", "what am i waiting on you for"))
            return new(GuardianCommandIntent.ListWatches);
        var cancelWatch = CancelWatchRegex().Match(exact);
        if (cancelWatch.Success) return new(GuardianCommandIntent.CancelWatch, cancelWatch.Groups["what"].Value.Trim());
        if (Nekomata.Core.Guardian.GuardianWatches.Parse(exact, DateTimeOffset.Now) is not null)
            return new(GuardianCommandIntent.AddWatch, exact);
        // Protocols: "end of day protocol", "run the morning protocol".
        if (Matches(exact, "list protocols", "what protocols do you have", "show protocols", "protocols", "what are my protocols", "which protocols are there"))
            return new(GuardianCommandIntent.ListProtocols);
        var createProtocol = CreateProtocolRegex().Match(exact);
        if (createProtocol.Success)
            return new(GuardianCommandIntent.CreateProtocol, createProtocol.Groups["name"].Value.Trim(), Detail: createProtocol.Groups["steps"].Value.Trim());
        var deleteProtocol = DeleteProtocolRegex().Match(exact);
        if (deleteProtocol.Success) return new(GuardianCommandIntent.DeleteProtocol, deleteProtocol.Groups["name"].Value.Trim());
        var protocol = RunProtocolRegex().Match(exact);
        if (protocol.Success)
            return new(GuardianCommandIntent.RunProtocol, protocol.Groups["name"].Value.Trim(), Detail: protocol.Groups["detail"].Value.Trim());
        if (Matches(exact, "stop calling me sir", "don't call me sir", "dont call me sir", "be less formal", "less formal",
                "drop the sir", "no need to call me sir", "you don't have to call me sir"))
            return new(GuardianCommandIntent.FormalManner, "off");
        if (Matches(exact, "call me sir", "be formal", "be more formal", "formal mode", "jarvis mode", "you can call me sir"))
            return new(GuardianCommandIntent.FormalManner, "on");
        if (exact.StartsWith("call me ", StringComparison.OrdinalIgnoreCase))
            return new(GuardianCommandIntent.FormalManner, "off");
        // Judgement: "anything to worry about?", "I'll handle these myself", and the answers to a challenge.
        if (Matches(exact, "anything i need to worry about", "anything to worry about", "anything i should worry about", "is there anything i need to worry about",
                "is there anything to worry about", "should i be worried", "should i be worried about anything", "anything i should be worried about",
                "am i needed", "do you need me", "does anything need me", "anything need me", "does anything need my attention", "anything need my attention",
                "is there anything i need to do", "anything i need to do", "can i relax", "is everything ok", "is everything okay", "are we ok", "are we okay",
                "anything i should be doing", "what should i worry about", "what do i need to worry about"))
            return new(GuardianCommandIntent.AnythingToWorry);
        if (Matches(exact, "do it", "go ahead", "yes do it", "yes, do it", "do that", "go on then", "yes go ahead", "yes, go ahead",
                "make it so", "add them", "add those tasks", "add it", "yes please do", "ok do it", "okay do it", "sounds good, do it"))
            return new(GuardianCommandIntent.ConfirmPending);
        if (Matches(exact, "autonomy settings", "open autonomy settings", "autonomy", "what can you do on your own",
                "what do you do on your own", "what are you allowed to do", "what can you do without asking"))
            return new(GuardianCommandIntent.AutonomySettings);
        if (WeatherRegex().IsMatch(exact)) return new(GuardianCommandIntent.Weather);
        var correctionMatch = CorrectionRegex().Match(text.Trim().Replace('\u2019', '\''));
        if (correctionMatch.Success) return new(GuardianCommandIntent.Correction, correctionMatch.Groups["note"].Value.Trim());
        if (FeedbackSummaryRegex().IsMatch(exact)) return new(GuardianCommandIntent.FeedbackSummary);
        if (Matches(exact, "settings", "guardian settings", "open settings", "open guardian settings", "preferences", "open preferences",
                "show settings", "change settings", "your settings"))
            return new(GuardianCommandIntent.GuardianSettings);
        if (Matches(exact, "what replies have you drafted", "what have you drafted", "show prepared replies", "prepared replies",
                "any drafts for me", "what drafts have you made", "what replies are ready"))
            return new(GuardianCommandIntent.ShowPreparedReplies);
        if (Matches(exact, "what have you learned about me", "what have you learnt about me", "what have you learned", "what have you learnt",
                "what do you know about how i work", "show what you've learned"))
            return new(GuardianCommandIntent.ShowLearned);
        if (Matches(exact, "undo learning", "reset learning", "forget what you've learned", "forget what you've learnt", "go back to defaults"))
            return new(GuardianCommandIntent.ResetLearning);
        if (Matches(exact, "what have i delegated", "what have i handed over", "show delegations", "what's delegated",
                "who's doing what for me", "what have i given out", "delegations"))
            return new(GuardianCommandIntent.ShowDelegations);
        if (Matches(exact, "follow up on my last meeting", "follow up my last meeting", "follow up on that meeting", "what came out of that meeting",
                "what came out of my last meeting", "meeting follow up", "meeting follow-up", "write up my last meeting"))
            return new(GuardianCommandIntent.MeetingFollowUp);
        if (Matches(exact, "enable meeting follow-through", "enable meeting follow through", "connect teams transcripts",
                "turn on meeting follow-through", "turn on meeting follow through", "allow meeting transcripts"))
            return new(GuardianCommandIntent.EnableMeetingFollowThrough);
        if (Matches(exact, "plan my day", "plan today", "plan the day", "plan the rest of my day", "plan the rest of the day",
                "lay out my day", "lay out today", "replan my day", "replan today", "sort my day out", "organise my day", "organize my day"))
            return new(GuardianCommandIntent.PlanMyDay);
        if (Matches(exact, "what am i not doing", "what am i avoiding", "what am i neglecting", "what's slipping", "what is slipping",
                "what am i forgetting", "what have i forgotten", "what am i dropping", "what's falling through the cracks",
                "what am i behind on", "what's been left", "what have i been putting off", "what am i putting off"))
            return new(GuardianCommandIntent.ShowNeglect);
        if (Matches(exact, "am i on track", "am i on schedule", "how am i doing", "how am i doing today", "how's my day going",
                "how is my day going", "am i behind", "what's my plan", "what is my plan", "what's the plan", "plan status",
                "where am i up to", "what should i be doing", "what should i be doing now"))
            return new(GuardianCommandIntent.PlanStatus);
        if (Matches(exact, "what did i promise today", "what have i promised today", "what did i commit to today",
                "what have i committed to today", "what did i say i'd do today", "what did i agree to today",
                "what did i promise people today", "what promises did i make today"))
            return new(GuardianCommandIntent.PromisesToday);
        if (Matches(exact, "read me my important emails", "read my important emails", "read me my emails", "read my emails",
                "what's important in my inbox", "what is important in my inbox", "anything important in my inbox",
                "any important emails", "any important email", "what emails need me", "which emails need me",
                "check my email", "check my emails", "what's in my inbox", "catch me up on email", "catch me up on my emails"))
            return new(GuardianCommandIntent.ReadImportantEmails);
        if (Matches(exact, "wrap up my day", "wrap up the day", "wrap up", "wrap up for the day", "end my day", "end of day",
                "close the day", "close out the day", "close out my day", "review my day", "how did today go"))
            return new(GuardianCommandIntent.WrapUpDay);
        if (Matches(exact, "show heads up", "show heads-up", "show the heads up", "show the heads-up", "show hud", "show the hud", "heads up on", "heads-up on"))
            return new(GuardianCommandIntent.ToggleHud, "show");
        if (Matches(exact, "hide heads up", "hide heads-up", "hide the heads up", "hide the heads-up", "hide hud", "hide the hud", "heads up off", "heads-up off"))
            return new(GuardianCommandIntent.ToggleHud, "hide");
        if (Matches(exact, "what changes did you make", "what did you change", "what have you changed",
                "what changes have you made", "what did guardian change"))
            return new(GuardianCommandIntent.ShowGuardianChanges);
        if (Matches(exact, "what needs my approval", "what is waiting for my approval", "show approvals",
                "what do i need to approve", "anything to approve"))
            return new(GuardianCommandIntent.ShowApprovals);
        if (Matches(exact, "what are you anticipating", "what are you preparing", "show anticipated actions",
                "what do you think i need next", "what happens next"))
            return new(GuardianCommandIntent.ShowAnticipatedActions);
        if (Matches(exact, "prepare me for my next meeting", "prepare me for the next meeting",
                "brief me for my next meeting", "what do i need for my next meeting",
                "get me ready for my next meeting", "get me ready for the next meeting"))
            return new(GuardianCommandIntent.PrepareNextMeeting);
        if (Matches(exact, "what can move forward now", "what is correctly waiting", "what's correctly waiting", "show next actions"))
            return new(GuardianCommandIntent.ShowThreadNextActions, exact);
        if (Matches(exact, "what will you handle next", "what are you reconsidering today",
                "what have you scheduled on my behalf", "why will guardian act on this", "show scheduled interventions"))
            return new(GuardianCommandIntent.ShowScheduledInterventions, exact);
        if (Matches(exact, "how is guardian performing", "how accurate are your predictions",
                "what did you get wrong", "how much admin time have you saved", "show prediction calibration"))
            return new(GuardianCommandIntent.ShowPredictionCalibration, exact);
        var match = NextActionRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.ShowThreadNextActions, text);
        if (Matches(exact, "approve prepared teams responses", "approve teams responses", "approve anticipated responses"))
            return new(GuardianCommandIntent.ApproveAnticipatedActions);
        if (Matches(exact, "send approved teams responses", "send approved responses", "send approved teams replies"))
            return new(GuardianCommandIntent.SendAnticipatedActions);
        match = CloseLoopRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.CancelDraftAndCloseLoop, match.Groups[1].Value.Trim());
        if (Matches(exact, "what needs attention", "show attention", "attention", "show my attention items",
                "what needs my attention", "what am i missing")) return new(GuardianCommandIntent.ShowAttention);
        if (Matches(exact, "why", "why that", "why is that", "why's that", "why is that first", "why's that first",
                "why is this first", "explain that", "explain why")) return new(GuardianCommandIntent.ExplainLastSurfaced);
        if (Matches(exact, "log interruption", "interrupt")) return new(GuardianCommandIntent.LogInterruption);
        if (Matches(exact, "plan tomorrow")) return new(GuardianCommandIntent.PlanTomorrow);
        if (Matches(exact, "what was i doing before lunch", "before lunch")) return new(GuardianCommandIntent.BeforeLunch);
        if (Matches(exact, "undo last guardian action", "undo last action", "undo")) return new(GuardianCommandIntent.UndoLastAction);
        if (Matches(exact, "email my status", "send my status", "email status to me", "send status to me")) return new(GuardianCommandIntent.EmailStatusToSelf);
        if (Matches(exact, "weekly report", "export weekly report", "export my week")) return new(GuardianCommandIntent.WeeklyReport);
        if (Matches(exact, "email weekly report", "send weekly report")) return new(GuardianCommandIntent.EmailWeeklyReport);
        if (Matches(exact, "show my work debt", "what keeps repeating", "what should i fix properly", "where am i firefighting", "what recurring work is costing me most",
                "what is my work debt", "what's my work debt", "what have i been putting off"))
            return new(GuardianCommandIntent.ShowWorkDebt);
        if (Matches(exact, "what do i owe")) return new(GuardianCommandIntent.ShowIOwe);
        if (Matches(exact, "who owes me something")) return new(GuardianCommandIntent.ShowOwedToMe);
        if (Matches(exact, "what commitments are at risk")) return new(GuardianCommandIntent.ShowAtRiskCommitments);
        if (Matches(exact, "what am i waiting on")) return new(GuardianCommandIntent.ShowWaitingCommitments);
        if (Matches(exact, "what have i promised this week")) return new(GuardianCommandIntent.ShowPromisedThisWeek);
        if (Matches(exact, "what commitments came from calls")) return new(GuardianCommandIntent.ShowCallCommitments);
        if (Matches(exact, "are you healthy")) return new(GuardianCommandIntent.ShowHealth);
        if (Matches(exact, "what integrations are unhealthy")) return new(GuardianCommandIntent.ShowUnhealthyIntegrations);
        if (Matches(exact, "when did you last sync")) return new(GuardianCommandIntent.ShowLastSync);
        if (Matches(exact, "overtime summary", "show my overtime", "what is my overtime", "what's my overtime",
                "how much overtime have i accrued", "how much additional time have i accrued", "additional time this week"))
            return new(GuardianCommandIntent.ShowWeeklyOvertime);
        if (Matches(exact, "executive value", "executive value summary", "show executive value",
                "investment portfolio", "show my investment portfolio", "where has my time gone",
                "what value have i generated", "how am i investing my time"))
            return new(GuardianCommandIntent.ShowExecutiveValue);
        if (Matches(exact, "what should i stop doing", "what am i spending time on that isn't worth me doing",
                "what am i spending time on that is not worth me doing", "show delegation opportunities",
                "what should i delegate", "what should i automate"))
            return new(GuardianCommandIntent.ShowStopDoing);
        if (Matches(exact, "where will my role alignment end this week", "forecast my role alignment"))
            return new(GuardianCommandIntent.ForecastRoleAlignment);
        if (Matches(exact, "how do i improve my role alignment", "improve my role alignment"))
            return new(GuardianCommandIntent.ImproveRoleAlignment);
        if (Matches(exact, "what is pulling me out of role", "what's pulling me out of role"))
            return new(GuardianCommandIntent.ExplainRoleDrift);
        if (Matches(exact, "which corrective action matters most", "what correction matters most"))
            return new(GuardianCommandIntent.ShowMostImportantCorrection);
        if (Matches(exact, "protect my strategic time"))
            return new(GuardianCommandIntent.ProtectStrategicTime);
        if (Matches(exact, "why is this planning bias active", "why is the planning bias active", "explain planning bias"))
            return new(GuardianCommandIntent.ExplainRoleBias);
        if (Matches(exact, "role alignment", "show role alignment", "how aligned am i to my role",
                "am i doing the right work", "is my work aligned to my role"))
            return new(GuardianCommandIntent.ShowRoleAlignment);
        if (Matches(exact, "protect my role", "what should i not take on", "what should i refuse",
                "where should i protect my role"))
            return new(GuardianCommandIntent.ProtectMyRole);
        if (Matches(exact, "product friction", "show product friction", "friction report")) return new(GuardianCommandIntent.ProductFriction);
        if (Matches(exact, "find me the next thing to do", "what should i do next", "next thing", "find next objective"))
            return new(GuardianCommandIntent.FindNextThing);

        var screen = ParseScreenAndHistory(text.TrimEnd('?', '!', '.').Replace('\u2019', '\''));
        if (screen is not null) return screen;
        match = InterruptionRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.LogInterruption, match.Groups[1].Value.Trim());
        match = EmailSearchRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.SearchEmail, match.Groups[1].Value.Trim());
        match = StartRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.StartMission, match.Groups[1].Value.Trim());
        match = RecommendationRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.ExplainRecommendation, match.Groups[1].Value.Trim());
        match = ScheduleRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.ScheduleEntity, match.Groups[1].Value.Trim());
        match = ProjectRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.ShowProject, match.Groups[1].Value.Trim());
        match = EmailRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.EmailsFrom, match.Groups[1].Value.Trim());
        match = FindTimeRegex().Match(text);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var minutes))
            return new(GuardianCommandIntent.FindTime, Minutes: minutes);
        match = WhyRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.ExplainDecision, match.Groups[1].Value.Trim());
        match = OpenRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.OpenEntity, match.Groups[1].Value.Trim());
        match = HistoryRegex().Match(text);
        if (match.Success) return new(GuardianCommandIntent.SearchHistory, match.Groups[1].Value.Trim());
        match = PersonBriefRegex().Match(text.TrimEnd('?', '.', '!'));
        if (match.Success) return new(GuardianCommandIntent.PersonBrief, match.Groups["who"].Value.Trim(), 0, text);
        return new(GuardianCommandIntent.Unknown, text);
    }

    // Corrections to "what's slipping": "that's not a promise", "Alex's isn't a promise", "that project's fine",
    // "those projects are fine", "that's done". Argument says what kind; Detail is whatever named the item, if anything.
    private static GuardianCommandMatch? ParseNeglectCorrection(string text)
    {
        var lower = text.ToLowerInvariant().Trim();
        if (Matches(lower, "that's done", "that is done", "that's already done", "that one's done", "i've done that", "i have done that",
                "already done", "i did that", "i already did that", "that's been done", "that's sorted", "that's dealt with"))
            return new(GuardianCommandIntent.NeglectCorrection, "done");
        var promise = NotAPromiseRegex().Match(lower);
        if (promise.Success)
            return new(GuardianCommandIntent.NeglectCorrection, "promise", 0, CleanCorrectionTarget(promise.Groups["who"].Value));
        var project = ProjectFineRegex().Match(lower);
        if (project.Success)
            return new(GuardianCommandIntent.NeglectCorrection, "project", 0, CleanCorrectionTarget(project.Groups["who"].Value));
        return null;
    }

    private static string CleanCorrectionTarget(string value)
    {
        var text = value.Trim().TrimEnd(',');
        text = Regex.Replace(text, @"^(?:no|nah|actually|well)[, ]+", "");
        text = Regex.Replace(text, @"^(?:that|this|it|the|those|these)\b\s*", "");
        text = Regex.Replace(text, @"(?:'s|’s)?\s*(?:one|promise|project|projects)?$", "");
        return text.Trim();
    }

    [GeneratedRegex(@"^(?<who>.*?)\s*(?:'s|’s|\s+is|\s+was)?\s*(?:not|n't|n’t)\s+(?:really\s+|actually\s+)?a\s+promise$")]
    private static partial Regex NotAPromiseRegex();
    [GeneratedRegex(@"^(?<who>.*?)\s*projects?\s*(?:'s|’s|\s+is|\s+are|\s+was|\s+were)\s+(?:fine|ok|okay|still\s+live|still\s+active|on\s+track|not\s+stalled|alive|moving)$")]
    private static partial Regex ProjectFineRegex();

    private static GuardianCommandMatch? ParseOperations(string exact)
    {
        if (Matches(exact, "are you working properly", "are you working", "self check", "self-check", "run a self check", "health check",
                "check yourself", "is everything connected", "are all your connections working", "systems check", "system check",
                "run a systems check", "run diagnostics", "diagnostics", "are all systems online", "systems status"))
            return new(GuardianCommandIntent.SelfCheck);
        if (Matches(exact, "what needs approving", "what needs my approval", "anything to approve", "approval requests", "what's waiting for my approval",
                "approval inbox", "what's waiting for approval", "anything need approving", "what do i need to approve"))
            return new(GuardianCommandIntent.ApprovalInbox);
        var approve = ApproveItRegex().Match(exact);
        if (approve.Success) return new(GuardianCommandIntent.ApproveIt, approve.Groups["r"].Value.Trim());
        var decline = DeclineItRegex().Match(exact);
        if (decline.Success) return new(GuardianCommandIntent.DeclineIt, decline.Groups["r"].Value.Trim());
        if (Matches(exact, "draft the project update", "draft the projects update", "weekly project update", "project update", "projects update",
                "draft a project update", "draft the weekly project update"))
            return new(GuardianCommandIntent.ProjectUpdate);
        var project = ProjectStatusRegex().Match(exact);
        if (project.Success) return new(GuardianCommandIntent.ProjectStatus, project.Groups["p"].Value.Trim());
        return null;
    }

    [GeneratedRegex(@"^(?:run|start|initiate|engage|begin|activate)?\s*(?:the\s+)?(?<name>[\p{L}\d' -]+?)\s+protocol(?:\s*[:,-]\s*(?<detail>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex RunProtocolRegex();

    private static GuardianCommandMatch? ParseMusic(string exact)
    {
        if (Matches(exact, "play music", "play some music", "music", "music please", "put some music on", "put music on", "start the music",
                "resume the music", "resume music", "resume spotify", "play spotify", "carry on playing", "unpause the music", "unpause"))
            return new(GuardianCommandIntent.Music, "play");
        if (Matches(exact, "pause the music", "pause music", "stop the music", "stop music", "pause spotify", "stop spotify", "mute the music",
                "kill the music", "music off", "hold the music"))
            return new(GuardianCommandIntent.Music, "pause");
        if (Matches(exact, "next song", "next track", "skip this song", "skip this track", "skip song", "skip the song", "skip track"))
            return new(GuardianCommandIntent.Music, "next");
        if (Matches(exact, "previous song", "previous track", "last song", "go back a song", "play that again", "back a track"))
            return new(GuardianCommandIntent.Music, "previous");
        if (Matches(exact, "shuffle on", "turn shuffle on", "shuffle", "shuffle the music")) return new(GuardianCommandIntent.Music, "shuffle", Detail: "on");
        if (Matches(exact, "shuffle off", "turn shuffle off", "stop shuffling")) return new(GuardianCommandIntent.Music, "shuffle", Detail: "off");
        if (Matches(exact, "what's playing", "whats playing", "what is playing", "what song is this", "what's this song", "what is this song",
                "what am i listening to", "what track is this", "what song is playing"))
            return new(GuardianCommandIntent.Music, "now");
        if (Matches(exact, "turn it up", "turn the music up", "louder", "volume up", "music up", "turn up the music"))
            return new(GuardianCommandIntent.Music, "volume", Detail: "+15");
        if (Matches(exact, "turn it down", "turn the music down", "quieter", "volume down", "music down", "turn down the music"))
            return new(GuardianCommandIntent.Music, "volume", Detail: "-15");
        var volume = MusicVolumeRegex().Match(exact);
        if (volume.Success) return new(GuardianCommandIntent.Music, "volume", Detail: volume.Groups["n"].Value);
        if (Matches(exact, "focus music", "play focus music", "play something to focus to", "play something for focus", "play my focus playlist",
                "play the focus playlist", "music to focus", "concentration music"))
            return new(GuardianCommandIntent.Music, "focus");
        var play = MusicPlayRegex().Match(exact);
        if (play.Success) return new(GuardianCommandIntent.Music, "search", Detail: play.Groups["q"].Value.Trim());
        return null;
    }

    [GeneratedRegex(@"^(?:set\s+(?:the\s+)?)?(?:volume|music)\s+(?:to\s+)?(?<n>\d{1,3})(?:\s*(?:%|percent))?$", RegexOptions.IgnoreCase)]
    private static partial Regex MusicVolumeRegex();

    [GeneratedRegex(@"^(?:play|put on)\s+(?<q>(?!back\b|it\b|that\b|hold\b|the\s+morning\b|the\s+brief)[\p{L}\d' &,.-]{2,60}?)(?:\s+on\s+spotify)?$", RegexOptions.IgnoreCase)]
    private static partial Regex MusicPlayRegex();

    [GeneratedRegex(@"^(?:stop watching|stop keeping an eye on|cancel the watch (?:on|for)|forget the watch (?:on|for)|don't watch|do not watch|cancel all watches|stop all watches)\s*(?<what>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CancelWatchRegex();

    [GeneratedRegex(@"^(?:create|add|new|make|set up)\s+(?:a\s+)?(?:new\s+)?protocol\s+(?:called\s+)?(?<name>[^:]+?)\s*:\s*(?<steps>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CreateProtocolRegex();

    [GeneratedRegex(@"^(?:delete|remove|drop|forget)\s+(?:the\s+)?(?<name>[\p{L}\d' -]+?)\s+protocol$", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteProtocolRegex();

    // Duties, objectives, time, meetings and office/home.
    private static GuardianCommandMatch? ParsePlanner(string exact, string raw)
    {
        if (PlannerRegex().IsMatch(exact)) return new(GuardianCommandIntent.Planner);
        if (Matches(exact, "how is guardian doing", "how's guardian doing", "guardian scorecard", "scorecard", "how reliable are you", "how accurate are you",
                "how have you done this week", "how did you do this week", "how are you doing guardian"))
            return new(GuardianCommandIntent.Scorecard, "week");
        if (Matches(exact, "how have you done this month", "how did you do this month", "guardian scorecard this month", "scorecard this month"))
            return new(GuardianCommandIntent.Scorecard, "month");
        if (Matches(exact, "what have you sent to the ai", "what have you sent to openai", "what did you send to the ai", "ai log", "ai privacy",
                "what's gone to the ai", "whats gone to the ai", "ai calls today", "privacy log"))
            return new(GuardianCommandIntent.AiPrivacyReport);
        var missedIssue = MissedIssueRegex().Match(raw.Trim());
        if (missedIssue.Success) return new(GuardianCommandIntent.MissedIssue, missedIssue.Groups["w"].Value.Trim());
        if (Matches(exact, "is guardian ok", "is guardian okay", "guardian health", "background jobs", "are your jobs running", "job health",
                "are your background jobs running", "is anything broken", "what's not working", "whats not working"))
            return new(GuardianCommandIntent.JobHealth);
        if (Matches(exact, "week in review", "my week in review", "weekly review", "review my week", "how did my week go", "how was my week",
                "friday review", "draft my week in review", "draft the week in review", "show my week in review"))
            return new(GuardianCommandIntent.WeekReview);
        if (Matches(exact, "setup check", "set up check", "test run", "monday test run", "what's left to set up", "what's left to setup", "is everything set up"))
            return new(GuardianCommandIntent.SetupCheck);
        var setupDone = SetupDoneRegex().Match(exact);
        if (setupDone.Success) return new(GuardianCommandIntent.SetupCheck, setupDone.Groups["n"].Value);
        if (Matches(exact, "morning brief", "morning briefing", "my morning brief", "daily brief", "start of day brief", "start of day briefing",
                "run the morning brief", "morning brief again", "what's my day look like", "what does my day look like", "how's my day looking"))
            return new(GuardianCommandIntent.MorningBrief);
        if (WhatsDueRegex().IsMatch(exact)) return new(GuardianCommandIntent.WhatsDue);
        var m = DutyDoneRegex().Match(exact);
        if (m.Success) return new(GuardianCommandIntent.DutyDone, m.Groups["d"].Value.Trim());
        m = AddObjectiveRegex().Match(raw);
        if (m.Success) return new(GuardianCommandIntent.AddObjective, m.Groups["o"].Value.Trim());
        if (ObjectiveEvidenceRegex().IsMatch(exact)) return new(GuardianCommandIntent.ObjectiveEvidence);
        if (LinkObjectivesRegex().IsMatch(exact)) return new(GuardianCommandIntent.LinkObjectives);
        if (ObjectivesRegex().IsMatch(exact)) return new(GuardianCommandIntent.Objectives);
        if (TimeWeekRegex().IsMatch(exact)) return new(GuardianCommandIntent.TimeWeek);
        if (TimeTodayRegex().IsMatch(exact)) return new(GuardianCommandIntent.TimeToday);
        if (MeetingReviewRegex().IsMatch(exact)) return new(GuardianCommandIntent.MeetingReview);
        if (MeetingLoadRegex().IsMatch(exact)) return new(GuardianCommandIntent.MeetingLoad);
        if (InOfficeRegex().IsMatch(exact)) return new(GuardianCommandIntent.Place, "office");
        if (AtHomeRegex().IsMatch(exact)) return new(GuardianCommandIntent.Place, "home");
        if (Matches(exact, "where am i", "am i in the office")) return new(GuardianCommandIntent.Place, "where");
        if (Matches(exact, "office reminders done", "clear the office reminders", "clear office reminders")) return new(GuardianCommandIntent.Place, "done");
        if (Matches(exact, "office reminders", "what's for the office", "what's on for the office", "anything for the office")) return new(GuardianCommandIntent.Place, "list");
        m = OfficeReminderRegex().Match(raw);
        if (m.Success) return new(GuardianCommandIntent.Place, "add:" + m.Groups["t"].Value.Trim().TrimEnd('.'));
        return null;
    }

    [GeneratedRegex(@"^(?:(?:open\s+)?(?:the\s+|my\s+)?planner|open\s+(?:my\s+)?duties)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlannerRegex();
    [GeneratedRegex(@"^(?:you\s+should\s+have\s+(?:caught|spotted|flagged|told\s+me\s+about)\s*(?:that|this|it)?|you\s+missed(?:\s+(?:that|this|something|one))?)\s*[:,\-]?\s*(?<w>.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MissedIssueRegex();
    [GeneratedRegex(@"^(?:what'?s\s+due(?:\s+this\s+week)?|(?:show\s+)?my\s+duties|(?:show\s+)?(?:the\s+)?duties|what\s+duties\s+are\s+due|any\s+duties\s+due|anything\s+due(?:\s+this\s+week)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex WhatsDueRegex();
    [GeneratedRegex(@"^(?:duty\s+done|mark\s+(?:the\s+)?duty\s+(?:as\s+)?done|done\s+(?:the\s+)?duty)\s*[:\-]?\s*(?<d>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DutyDoneRegex();
    [GeneratedRegex(@"^(?:add|new)\s+(?:an\s+)?objective\s*[:\-]?\s*(?<o>.{4,})$", RegexOptions.IgnoreCase)]
    private static partial Regex AddObjectiveRegex();
    [GeneratedRegex(@"^(?:(?:show\s+)?(?:my\s+)?objectives|how\s+am\s+i\s+doing\s+(?:against|on|with)\s+(?:my\s+)?objectives|objectives?\s+progress|am\s+i\s+on\s+track\s+(?:with|for)\s+(?:my\s+)?objectives)$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectivesRegex();
    [GeneratedRegex(@"^(?:objectives?\s+evidence|appraisal\s+evidence|export\s+(?:my\s+)?objectives|(?:my\s+)?evidence\s+for\s+(?:my\s+)?appraisal)$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectiveEvidenceRegex();
    [GeneratedRegex(@"^link\s+(?:my\s+)?work\s+to\s+(?:my\s+)?objectives$", RegexOptions.IgnoreCase)]
    private static partial Regex LinkObjectivesRegex();
    [GeneratedRegex(@"^(?:where\s+(?:did|has)\s+my\s+time\s+(?:go|gone)(?:\s+today)?|time\s+today|how\s+did\s+i\s+spend\s+(?:my\s+time\s*)?today|how\s+did\s+i\s+spend\s+my\s+time)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeTodayRegex();
    [GeneratedRegex(@"^(?:time\s+this\s+week|where\s+(?:did|has)\s+my\s+(?:time|week)\s+(?:go|gone)\s+this\s+week|where\s+did\s+my\s+week\s+go|how\s+reactive\s+(?:was|is|has)\s+(?:my|this)\s+week(?:\s+been)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeWeekRegex();
    [GeneratedRegex(@"^(?:review\s+my\s+(?:recurring\s+)?meetings|(?:recurring\s+)?meeting\s+review|which\s+meetings\s+should\s+i\s+drop)$", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingReviewRegex();
    [GeneratedRegex(@"^(?:meeting\s+load|how\s+many\s+meetings\s+(?:have\s+i\s+(?:got\s+)?|do\s+i\s+have\s+)?(?:this|next)\s+week|how\s+busy\s+is\s+next\s+week)$", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLoadRegex();
    [GeneratedRegex(@"^(?:i'?m|i\s+am)\s+(?:in\s+(?:the\s+)?office|at\s+the\s+office|in\s+work|on\s+site)$", RegexOptions.IgnoreCase)]
    private static partial Regex InOfficeRegex();
    [GeneratedRegex(@"^(?:i'?m|i\s+am)\s+(?:at\s+home|working\s+from\s+home|wfh)$", RegexOptions.IgnoreCase)]
    private static partial Regex AtHomeRegex();
    [GeneratedRegex(@"^(?:when\s+i'?m\s+next\s+in(?:\s+the\s+office)?|next\s+time\s+i'?m\s+in(?:\s+the\s+office)?|remind\s+me\s+(?:when\s+i'?m\s+)?in\s+the\s+office(?:\s+to)?)\s*[:,\-]?\s*(?<t>.{3,})$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficeReminderRegex();
    [GeneratedRegex(@"^setup\s+(?:step\s+)?done\s*[:\-]?\s*(?<n>\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex SetupDoneRegex();
    [GeneratedRegex(@"^(?:no[,.!]?\s+)?(?:that(?:'s|\s+was|\s+is)\s+(?:wrong|not\s+right|incorrect|not\s+correct|not\s+true)|wrong\s+answer|you'?re\s+wrong|you\s+got\s+(?:that|it)\s+wrong)(?:\s*[:,;\-\u2013\u2014.]\s*(?<note>.+)|\s+(?<note>(?:it'?s|it\s+is|it\s+was|actually|the)\b.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CorrectionRegex();
    [GeneratedRegex(@"^(?:how\s+am\s+i\s+rating\s+you|(?:show\s+)?(?:my\s+)?feedback(?:\s+summary)?|what\s+have\s+you\s+learn(?:ed|t)\s+from\s+my\s+feedback|how\s+(?:are\s+you|have\s+you\s+been)\s+doing\s+on\s+my\s+feedback)$", RegexOptions.IgnoreCase)]
    private static partial Regex FeedbackSummaryRegex();
    [GeneratedRegex(@"^approve (?:it|that|this|this one|that one)(?:\s*[,:-]?\s*(?:because|but|and say|saying)?\s*(?<r>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ApproveItRegex();
    [GeneratedRegex(@"^(?:decline|reject|refuse|deny) (?:it|that|this|this one|that one)(?:\s*[,:-]?\s*(?:because|as|since)?\s*(?<r>.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DeclineItRegex();
    [GeneratedRegex(@"^(?:project status(?: for| of| on)?|status of the|status on the|where are we (?:on|with)(?: the)?)\s+(?<p>.+?)(?:\s+project)?$|^how(?:'s| is) the (?<p>.+?) project(?: going| doing)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectStatusRegex();

    private static GuardianCommandMatch? ParseScreenAndHistory(string exact)
    {
        var operations = ParseOperations(exact);
        if (operations is not null) return operations;
        var capability = CapabilitiesRegex().Match(exact);
        if (capability.Success || Matches(exact, "help", "what can you do", "what can i ask you", "what can you help with"))
            return new(GuardianCommandIntent.Capabilities, capability.Success ? capability.Groups["topic"].Value.Trim() : "");
        if (Matches(exact, "review my invites", "review invites", "any invites", "what invites have i got", "unanswered invites",
                "check my invites", "what meeting invites have i got", "any meeting invites"))
            return new(GuardianCommandIntent.ReviewInvites);
        var open = OpenSourceRegex().Match(exact);
        if (open.Success) return new(GuardianCommandIntent.OpenHistorySource, open.Groups[1].Value, int.Parse(open.Groups[1].Value));
        if (ScreenThisRegex().IsMatch(exact) || Matches(exact, "what's on my screen", "what is on my screen", "look at my screen", "look at this"))
            return new(GuardianCommandIntent.ScreenThis, exact);
        if (AskHistoryRegex().IsMatch(exact) && !exact.Contains("next thing", StringComparison.OrdinalIgnoreCase) &&
            !exact.Contains("free time", StringComparison.OrdinalIgnoreCase) && !exact.Contains("a slot", StringComparison.OrdinalIgnoreCase))
            return new(GuardianCommandIntent.AskHistory, exact);
        return null;
    }

    [GeneratedRegex(@"^what (?:else )?(?:can|could) (?:you|i ask you to|i get you to) (?:do|help(?: me)? with)(?:\s+(?:in|with|for|on|about|while|when))?\s*(?<topic>.*)$|^what can i ask you(?:\s+about)?\s*(?<topic>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CapabilitiesRegex();
    [GeneratedRegex(@"^open\s+(?:source|result)\s*#?(\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OpenSourceRegex();
    [GeneratedRegex(@"^(?:can you\s+|could you\s+|please\s+|guardian,?\s+)?(?:reply to|respond to|answer|summari[sz]e|explain|read|translate|fix|check|deal with|action|turn|make|add|remember|save|note|what(?:'s| is| does| do)|who(?:'s| is| sent)|how do i|what should i|help me with|draft a reply to|look at|sort)\b.*\b(?:this|these|on (?:my|the) screen)\b(?!\s+(?:week|weekend|morning|afternoon|evening|month|year|time|quarter|term)\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenThisRegex();
    [GeneratedRegex(@"^(?:when did (?:we|i|someone|anyone|they)|when was the last time|has anyone (?:had|reported|seen|asked|logged)|have we (?:ever|had|done|got)|did we (?:ever )?(?:have|get|agree|buy|renew|change|order|decide)|what did (?:we|i|they) (?:agree|decide|say|buy|order|pay|do)|who (?:fixed|sorted|dealt with|handled|looked at)|how did we (?:fix|sort|solve|deal with)|search (?:for|my history for|everything for)|look up|find (?:me )?(?:the|that|anything|everything|emails? about|messages? about|where)|do we have (?:a|any)\b)\s+.+", RegexOptions.IgnoreCase)]
    private static partial Regex AskHistoryRegex();

    private static GuardianCommandMatch? ParseTeams(string exact)
    {
        if (Matches(exact, "catch me up on teams", "what's new on teams", "what is new on teams", "what's new in teams", "anything on teams",
                "anything new on teams", "anything new in teams", "teams catch up", "teams catch-up", "teams updates", "teams update",
                "what have i missed on teams", "what did i miss on teams", "what's happening on teams", "what's going on in teams",
                "what's going on on teams", "check teams", "check my teams", "check my teams messages", "any teams messages",
                "anything in my chats", "what's new in my chats", "catch me up on my chats"))
            return new(GuardianCommandIntent.TeamsCatchUp);
        if (Matches(exact, "what did i miss", "what have i missed", "what've i missed", "catch me up", "what's happened", "what has happened",
                "what happened while i was away", "what happened while i was out", "what happened in my meeting", "anything happen",
                "did anything happen", "since i last looked", "what's new", "what's changed", "anything i need to know"))
            return new(GuardianCommandIntent.WhatDidIMiss);
        if (Matches(exact, "who are my managers", "who is my manager", "who's my manager", "who are the managers", "list my managers"))
            return new(GuardianCommandIntent.TeamsManager, "", Detail: "list");
        var from = TeamsFromRegex().Match(exact);
        if (from.Success)
        {
            var who = from.Groups["who"].Value.Trim();
            var onTeams = from.Groups["teams"].Success || exact.StartsWith("what", StringComparison.OrdinalIgnoreCase);
            if (who.Length > 0 && (onTeams || ManagerWordsRegex().IsMatch(who)))
                return new(GuardianCommandIntent.TeamsFrom, who);
        }
        var add = ManagerAddRegex().Match(exact);
        if (add.Success && !Matches(add.Groups["who"].Value.Trim(), "who", "who's", "he", "she", "they"))
            return new(GuardianCommandIntent.TeamsManager, add.Groups["who"].Value.Trim(), Detail: "add");
        var remove = ManagerRemoveRegex().Match(exact);
        if (remove.Success)
            return new(GuardianCommandIntent.TeamsManager, remove.Groups["who"].Value.Trim(), Detail: "remove");
        return null;
    }

    [GeneratedRegex(@"^(?:(?:is there|are there|have i had|have i got)\s+)?(?:anything|any messages?|any chats?)(?:\s+new)?\s+from\s+(?<who>.+?)(?<teams>\s+(?:on|in)\s+teams)?$|^what(?:'s| has| did| have)\s+(?<who>.+?)\s+(?:said|sent|say|been saying|sent me|said to me)(?<teams>\s+(?:on|in)\s+teams)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TeamsFromRegex();
    [GeneratedRegex(@"^(?:my\s+|the\s+)?(?:line\s+)?(?:manager|managers|boss|bosses|management|leadership)$", RegexOptions.IgnoreCase)]
    private static partial Regex ManagerWordsRegex();
    [GeneratedRegex(@"^(?:treat|mark|add)\s+(?<who>.+?)\s+as\s+(?:a\s+|my\s+|one of my\s+)?(?:manager|boss|senior manager|director)$|^(?<who>.+?)\s+is\s+(?:a|my|one of my)\s+(?:line\s+)?(?:manager|managers|boss|director)$", RegexOptions.IgnoreCase)]
    private static partial Regex ManagerAddRegex();
    [GeneratedRegex(@"^(?:stop treating|don't treat|do not treat)\s+(?<who>.+?)\s+as\s+a\s+manager$|^(?<who>.+?)\s+(?:isn't|is not)\s+(?:a|my)\s+manager$", RegexOptions.IgnoreCase)]
    private static partial Regex ManagerRemoveRegex();

    private static bool Matches(string value, params string[] options) =>
        options.Any(option => value.Equals(option, StringComparison.OrdinalIgnoreCase));

    // "quiet for an hour", "do not disturb until 3", "mute for 20 minutes" -> QuietMode with Minutes, or Argument
    // "until:HH:mm" (24h; a bare 1-7 is read as the afternoon, this being a work assistant); "you can talk again" -> "off".
    private static GuardianCommandMatch? ParseQuietMode(string text)
    {
        if (QuietOffRegex().IsMatch(text)) return new(GuardianCommandIntent.QuietMode, "off");
        if (WorkingAnywayRegex().IsMatch(text)) return new(GuardianCommandIntent.QuietMode, "working");
        if (OffDutyRegex().IsMatch(text)) return new(GuardianCommandIntent.QuietMode, "offduty");
        if (OffHoursDisableRegex().IsMatch(text)) return new(GuardianCommandIntent.QuietMode, "disable");
        var match = QuietOnRegex().Match(text);
        if (!match.Success) return null;
        if (match.Groups["until"].Success)
        {
            var until = match.Groups["until"].Value.Trim().ToLowerInvariant();
            var pm = until.EndsWith("pm");
            var am = until.EndsWith("am");
            var clock = until.Replace("am", "").Replace("pm", "").Trim();
            var parts = clock.Split(':');
            if (!int.TryParse(parts[0], out var hour) || hour > 23) return null;
            var minute = parts.Length > 1 && int.TryParse(parts[1], out var m) && m < 60 ? m : 0;
            if (pm && hour < 12) hour += 12;
            else if (!am && hour >= 1 && hour <= 7) hour += 12;
            return new(GuardianCommandIntent.QuietMode, $"until:{hour:00}:{minute:00}");
        }
        var duration = match.Groups["dur"].Success ? match.Groups["dur"].Value.Trim().ToLowerInvariant() : "";
        var minutes = duration switch
        {
            "" => 60,
            var d when d.StartsWith("half") => 30,
            var d when d.StartsWith("an hour") || d.StartsWith("a hour") => 60,
            var d when DurationRegex().Match(d) is { Success: true } part =>
                int.Parse(part.Groups[1].Value) * (part.Groups[2].Value.StartsWith('h') ? 60 : 1),
            _ => 60
        };
        return new(GuardianCommandIntent.QuietMode, "on", Math.Clamp(minutes, 1, 12 * 60));
    }

    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:please\s+)?(?:be\s+quiet|go\s+quiet|quiet(?:\s+mode)?(?:\s+on)?|mute(?:\s+yourself)?|do\s+not\s+disturb|don'?t\s+disturb\s+me|dnd)(?:\s+(?:for\s+)?(?<dur>half\s+an\s+hour|an?\s+hour|\d+\s*(?:minutes?|mins?|hours?|hrs?|h|m))|\s+until\s+(?<until>\d{1,2}(?::\d{2})?\s*(?:am|pm)?))?$", RegexOptions.IgnoreCase)] private static partial Regex QuietOnRegex();
    [GeneratedRegex(@"^(?:what'?s|what\s+is|how'?s|how\s+is)\s+(?:the\s+)?weather(?:\s+(?:like|today|now|doing|outside|this\s+afternoon|tomorrow))*$|^weather(?:\s+(?:today|now|forecast))?$|^(?:is\s+it|will\s+it|is\s+it\s+going\s+to)\s+(?:rain|raining|snow|snowing)(?:\s+(?:today|later|this\s+afternoon|at\s+home\s+time|tomorrow))?$|^do\s+i\s+need\s+(?:a\s+coat|an\s+umbrella|a\s+brolly)(?:\s+(?:today|later))?$|^what'?s\s+it\s+like\s+outside$", RegexOptions.IgnoreCase)] private static partial Regex WeatherRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:i'?m|i\s+am)\s+(?:working|on\s+duty|on\s+call)(?:\s+(?:this\s+weekend|today|tonight|late|now|for\s+a\s+bit))?$|^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:work(?:ing)?\s+mode|on\s+duty)$", RegexOptions.IgnoreCase)] private static partial Regex WorkingAnywayRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:(?:go\s+)?off\s+duty|stand\s+down|i'?m\s+off\s+duty|out\s+of\s+hours\s+mode(?:\s+on)?|(?:weekend|off[\s-]hours)\s+mode(?:\s+on)?)$", RegexOptions.IgnoreCase)] private static partial Regex OffDutyRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:(?:turn\s+)?(?:out\s+of\s+hours|weekend|off[\s-]hours)\s+mode\s+off|disable\s+(?:out\s+of\s+hours|weekend|off[\s-]hours)\s+mode)$", RegexOptions.IgnoreCase)] private static partial Regex OffHoursDisableRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:quiet\s+mode\s+off|unmute|you\s+can\s+(?:talk|speak)(?:\s+(?:again|now))?|voice\s+back\s+on|end\s+(?:quiet\s+mode|do\s+not\s+disturb)|stop\s+being\s+quiet|(?:do\s+not\s+disturb|dnd)\s+off)$", RegexOptions.IgnoreCase)] private static partial Regex QuietOffRegex();
    [GeneratedRegex(@"(\d+)\s*(h|m)")] private static partial Regex DurationRegex();

    // "remember that Sarah prefers calls" (not "remember to ...", which is a task, not a fact).
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:please\s+)?remember\s*(?::\s*|\s+that\s+|\s+(?!to\s))(.{3,})$", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex RememberRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:please\s+)?forget\s+(?:that\s+|about\s+)?(?!it$|that$|this$)(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ForgetRegex();
    // "reply to Sarah's email saying I'll look at it Thursday" / "draft a reply to John Smith's last email: thanks, approved".
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:draft\s+(?:a\s+)?|write\s+(?:a\s+)?)?(?:reply|respond|write\s+back|get\s+back|email\s+back)\s+to\s+(?:the\s+(?:last\s+|latest\s+)?(?:e-?mail|mail|message)\s+from\s+)?(?<who>.+?)(?:(?:'s|s'|’s)\s+(?:last\s+|latest\s+|recent\s+)?(?:e-?mail|mail|message))?(?:\s+for\s+me)?(?:\s*[:,-]\s*|\s+(?:and\s+)?(?:saying|to\s+say|say|(?:tell|telling)\s+(?:him|her|them)|(?:let|letting)\s+(?:him|her|them)\s+know|that)\s+(?:that\s+)?)(?<what>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ReplyRegex();

    // "Note that Sarah will send the quote by Friday" / "make a note: renew the SSL cert".
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:please\s+)?(?:make\s+a\s+note(?:\s+that)?|note\s+down(?:\s+that)?|note\s+that|note)\s*[:,-]?\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex MeetingNoteRegex();
    [GeneratedRegex(@"^(?:start|begin|focus on)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex StartRegex();
    [GeneratedRegex(@"^(?:show|open)\s+project\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex ProjectRegex();
    [GeneratedRegex(@"^(?:emails?|mail)\s+from\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex EmailRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:(?:can|could)\s+you\s+)?(?:please\s+)?(?:delegate|give|hand(?:\s+over)?|pass)\s+(?!me\b|it\s+back\b)(?<what>.+?)\s+(?:over\s+)?to\s+(?<who>[\p{L}][\p{L}'.\-@ ]{1,60}?)(?:[,]?\s+(?:by|for|before|due)\s+(?<when>.+))?$", RegexOptions.IgnoreCase)] private static partial Regex DelegateRegex();
    [GeneratedRegex(@"^(?:(?:hey\s+)?guardian[,:]?\s+)?(?:(?:can|could)\s+you\s+)?(?:please\s+)?(?:brief\s+me\s+on|catch\s+me\s+up\s+on|who\s+is|who's|what\s+do\s+i\s+need\s+to\s+know\s+about|what(?:'s|\s+is)\s+(?:the\s+)?(?:latest|happening|going\s+on)\s+with|anything\s+(?:new\s+)?from)\s+(?<who>[\p{L}][\p{L}'.\- ]{1,59})$", RegexOptions.IgnoreCase)] private static partial Regex PersonBriefRegex();
    [GeneratedRegex(@"^find me\s+([0-9]+)\s*(?:minutes?|mins?|m)\s+tomorrow$", RegexOptions.IgnoreCase)] private static partial Regex FindTimeRegex();
    [GeneratedRegex(@"^why did (?:you|guardian)\s+(?:move|defer|reschedule)\s+(.+?)[?]?$", RegexOptions.IgnoreCase)] private static partial Regex WhyRegex();
    [GeneratedRegex(@"^why is\s+(.+?)\s+recommended[?]?$", RegexOptions.IgnoreCase)] private static partial Regex RecommendationRegex();
    [GeneratedRegex(@"^(?:schedule|plan)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex ScheduleRegex();
    [GeneratedRegex(@"^(?:open|show)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex OpenRegex();
    [GeneratedRegex(@"^(?:history|journal|chronicle)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex HistoryRegex();
    [GeneratedRegex(@"^(?:interrupt|log interruption)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex InterruptionRegex();
    [GeneratedRegex(@"^(?:search mail|search email|mail search|email search)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex EmailSearchRegex();
    [GeneratedRegex(@"^(?:remove|cancel)\s+(?:the\s+)?(?:draft|follow[- ]?up)(?:\s+(?:for|to))?\s+(.+?)\s+and\s+(?:close|resolve|mark)\s+(?:the\s+)?loop(?:\s+closed)?[?]?$", RegexOptions.IgnoreCase)] private static partial Regex CloseLoopRegex();
    [GeneratedRegex(@"^what happens next for\s+(.+?)[?]?$", RegexOptions.IgnoreCase)] private static partial Regex NextActionRegex();
}
