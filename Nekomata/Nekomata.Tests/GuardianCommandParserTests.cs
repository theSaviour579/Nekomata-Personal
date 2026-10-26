using Nekomata.Core.Guardian.Commands;
using Xunit;

namespace Nekomata.Tests;

public sealed class GuardianCommandParserTests
{
    [Fact]
    public void Parse_HistorySearch_UsesChronicleIntent()
    {
        var result = GuardianCommandParser.Parse("history pricing project");

        Assert.Equal(GuardianCommandIntent.SearchHistory, result.Intent);
        Assert.Equal("pricing project", result.Argument);
    }

    [Theory]
    [InlineData("note that Sarah will send the quote by Friday", "Sarah will send the quote by Friday")]
    [InlineData("Guardian, note that we agreed to pilot the new layout", "we agreed to pilot the new layout")]
    [InlineData("make a note: renew the SSL certificate", "renew the SSL certificate")]
    [InlineData("make a note that Tom owns the rollout", "Tom owns the rollout")]
    [InlineData("note down budget approved", "budget approved")]
    public void Parse_MeetingNote_ExtractsNoteText(string input, string note)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.NoteForMeeting, result.Intent);
        Assert.Equal(note, result.Argument);
    }

    [Theory]
    [InlineData("quiet for an hour", "on", 60)]
    [InlineData("Guardian, be quiet for 20 minutes", "on", 20)]
    [InlineData("do not disturb for 2 hours", "on", 120)]
    [InlineData("mute for half an hour", "on", 30)]
    [InlineData("quiet mode", "on", 60)]
    [InlineData("do not disturb until 3", "until:15:00", 0)]
    [InlineData("quiet until 9:30am", "until:09:30", 0)]
    [InlineData("dnd until 16:45", "until:16:45", 0)]
    [InlineData("you can talk again", "off", 0)]
    [InlineData("Guardian, unmute.", "off", 0)]
    [InlineData("I'm working this weekend", "working", 0)]
    [InlineData("I'm working late", "working", 0)]
    [InlineData("on duty", "working", 0)]
    [InlineData("off duty", "offduty", 0)]
    [InlineData("Guardian, stand down.", "offduty", 0)]
    [InlineData("weekend mode", "offduty", 0)]
    [InlineData("out of hours mode off", "disable", 0)]
    public void Parse_QuietMode_ReadsDurationsAndTimes(string input, string argument, int minutes)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.QuietMode, result.Intent);
        Assert.Equal(argument, result.Argument);
        Assert.Equal(minutes, result.Minutes);
    }

    [Theory]
    [InlineData("what's the weather")]
    [InlineData("What's the weather like today?")]
    [InlineData("is it going to rain")]
    [InlineData("do I need a coat")]
    public void Parse_Weather(string input) => Assert.Equal(GuardianCommandIntent.Weather, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("That was wrong: the VPN is Rob's, not Sam's", "the VPN is Rob's, not Sam's")]
    [InlineData("no, that's not right, it's managed by Kevin", "it's managed by Kevin")]
    [InlineData("that's wrong", "")]
    [InlineData("wrong answer - use the latest figures", "use the latest figures")]
    public void Parse_Correction(string input, string note)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.Correction, result.Intent);
        Assert.Equal(note, result.Argument);
    }

    [Theory]
    [InlineData("how am I rating you?")]
    [InlineData("what have you learned from my feedback")]
    public void Parse_FeedbackSummary(string input) => Assert.Equal(GuardianCommandIntent.FeedbackSummary, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    public void Parse_new_operations(string input, GuardianCommandIntent intent) => Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("what's due?", GuardianCommandIntent.WhatsDue, "")]
    [InlineData("duty done: patch Tuesday", GuardianCommandIntent.DutyDone, "patch Tuesday")]
    [InlineData("Add objective: halve repeat printer tickets by March", GuardianCommandIntent.AddObjective, "halve repeat printer tickets by March")]
    [InlineData("how am I doing against my objectives", GuardianCommandIntent.Objectives, "")]
    [InlineData("appraisal evidence", GuardianCommandIntent.ObjectiveEvidence, "")]
    [InlineData("where did my time go?", GuardianCommandIntent.TimeToday, "")]
    [InlineData("time this week", GuardianCommandIntent.TimeWeek, "")]
    [InlineData("review my recurring meetings", GuardianCommandIntent.MeetingReview, "")]
    [InlineData("how many meetings have I got next week", GuardianCommandIntent.MeetingLoad, "")]
    [InlineData("I'm in the office", GuardianCommandIntent.Place, "office")]
    [InlineData("I'm working from home", GuardianCommandIntent.Place, "home")]
    [InlineData("When I'm next in: swap the monitor in finance", GuardianCommandIntent.Place, "add:swap the monitor in finance")]
    [InlineData("planner", GuardianCommandIntent.Planner, "")]
    [InlineData("morning brief", GuardianCommandIntent.MorningBrief, "")]
    [InlineData("setup check", GuardianCommandIntent.SetupCheck, "")]
    [InlineData("setup done: 18", GuardianCommandIntent.SetupCheck, "18")]
    [InlineData("What does my day look like?", GuardianCommandIntent.MorningBrief, "")]
    public void Parse_planner(string input, GuardianCommandIntent intent, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("Guardian settings")]
    [InlineData("open settings")]
    public void Parse_Settings(string input) => Assert.Equal(GuardianCommandIntent.GuardianSettings, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("That wasn't important.", "less")]
    [InlineData("don't bother me with those", "less")]
    [InlineData("always tell me about those", "more")]
    [InlineData("reset my interruption preferences", "reset")]
    public void Parse_InterruptionFeedback(string input, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.InterruptionFeedback, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Theory]
    [InlineData("wrap up my day")]
    [InlineData("End my day.")]
    [InlineData("how did today go?")]
    public void Parse_WrapUpDay(string input) =>
        Assert.Equal(GuardianCommandIntent.WrapUpDay, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("show heads up", "show")]
    [InlineData("Show the HUD", "show")]
    [InlineData("hide heads-up", "hide")]
    public void Parse_HeadsUp_TogglesOverlay(string input, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.ToggleHud, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("what needs attention")]
    public void Parse_NonNotes_AreNotMeetingNotes(string input) =>
        Assert.NotEqual(GuardianCommandIntent.NoteForMeeting, GuardianCommandParser.Parse(input).Intent);

    [Fact]
    public void Parse_EmailMyStatus_UsesAwayStatusIntent() =>
        Assert.Equal(GuardianCommandIntent.EmailStatusToSelf, GuardianCommandParser.Parse("email my status").Intent);

    [Theory]
    [InlineData("interrupt Rhys - Entra issue", GuardianCommandIntent.LogInterruption, "Rhys - Entra issue")]
    [InlineData("search email quarterly pricing", GuardianCommandIntent.SearchEmail, "quarterly pricing")]
    public void Parse_ExtractsNewCaptureAndSearchCommands(string input, GuardianCommandIntent intent, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Theory]
    [InlineData("weekly report", GuardianCommandIntent.WeeklyReport)]
    [InlineData("email weekly report", GuardianCommandIntent.EmailWeeklyReport)]
    [InlineData("product friction", GuardianCommandIntent.ProductFriction)]
    [InlineData("find me the next thing to do", GuardianCommandIntent.FindNextThing)]
    [InlineData("how much overtime have I accrued?", GuardianCommandIntent.ShowWeeklyOvertime)]
    [InlineData("how am I investing my time?", GuardianCommandIntent.ShowExecutiveValue)]
    [InlineData("what should I stop doing?", GuardianCommandIntent.ShowStopDoing)]
    [InlineData("role alignment", GuardianCommandIntent.ShowRoleAlignment)]
    [InlineData("protect my role", GuardianCommandIntent.ProtectMyRole)]
    [InlineData("Where will my role alignment end this week?", GuardianCommandIntent.ForecastRoleAlignment)]
    [InlineData("How do I improve my role alignment?", GuardianCommandIntent.ImproveRoleAlignment)]
    [InlineData("What is pulling me out of role?", GuardianCommandIntent.ExplainRoleDrift)]
    [InlineData("Which corrective action matters most?", GuardianCommandIntent.ShowMostImportantCorrection)]
    [InlineData("Protect my strategic time.", GuardianCommandIntent.ProtectStrategicTime)]
    [InlineData("Why is this planning bias active?", GuardianCommandIntent.ExplainRoleBias)]
    [InlineData("Show my work debt", GuardianCommandIntent.ShowWorkDebt)]
    [InlineData("What keeps repeating?", GuardianCommandIntent.ShowWorkDebt)]
    [InlineData("What do I owe?", GuardianCommandIntent.ShowIOwe)]
    [InlineData("Who owes me something?", GuardianCommandIntent.ShowOwedToMe)]
    [InlineData("What commitments are at risk?", GuardianCommandIntent.ShowAtRiskCommitments)]
    [InlineData("What am I waiting on?", GuardianCommandIntent.ShowWaitingCommitments)]
    [InlineData("What have I promised this week?", GuardianCommandIntent.ShowPromisedThisWeek)]
    [InlineData("What commitments came from calls?", GuardianCommandIntent.ShowCallCommitments)]
    [InlineData("Are you healthy?", GuardianCommandIntent.ShowHealth)]
    [InlineData("What integrations are unhealthy?", GuardianCommandIntent.ShowUnhealthyIntegrations)]
    [InlineData("When did you last sync?", GuardianCommandIntent.ShowLastSync)]
    public void Parse_RecognisesWeeklyReportCommands(string input, GuardianCommandIntent intent) =>
        Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("start YoY spend", GuardianCommandIntent.StartMission, "YoY spend")]
    [InlineData("show project CHT", GuardianCommandIntent.ShowProject, "CHT")]
    [InlineData("emails from Craig", GuardianCommandIntent.EmailsFrom, "Craig")]
    [InlineData("why did you move YoY Spend?", GuardianCommandIntent.ExplainDecision, "YoY Spend")]
    [InlineData("open Category Review", GuardianCommandIntent.OpenEntity, "Category Review")]
    [InlineData("schedule Sell Pack Target Creation", GuardianCommandIntent.ScheduleEntity, "Sell Pack Target Creation")]
    [InlineData("why is YoY Spend recommended?", GuardianCommandIntent.ExplainRecommendation, "YoY Spend")]
    public void Parse_ExtractsEntityCommands(string input, GuardianCommandIntent intent, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Fact]
    public void Parse_ExtractsRequestedCalendarDuration()
    {
        var result = GuardianCommandParser.Parse("find me 90 minutes tomorrow");
        Assert.Equal(GuardianCommandIntent.FindTime, result.Intent);
        Assert.Equal(90, result.Minutes);
    }

    [Theory]
    [InlineData("brief me", GuardianCommandIntent.BriefMe)]
    [InlineData("what needs attention?", GuardianCommandIntent.ShowAttention)]
    [InlineData("log interruption", GuardianCommandIntent.LogInterruption)]
    [InlineData("plan tomorrow", GuardianCommandIntent.PlanTomorrow)]
    [InlineData("what was I doing before lunch?", GuardianCommandIntent.BeforeLunch)]
    [InlineData("undo last guardian action", GuardianCommandIntent.UndoLastAction)]
    [InlineData("what changes did you make?", GuardianCommandIntent.ShowGuardianChanges)]
    [InlineData("what needs my approval?", GuardianCommandIntent.ShowApprovals)]
    [InlineData("what are you anticipating?", GuardianCommandIntent.ShowAnticipatedActions)]
    [InlineData("approve prepared Teams responses", GuardianCommandIntent.ApproveAnticipatedActions)]
    [InlineData("send approved Teams responses", GuardianCommandIntent.SendAnticipatedActions)]
    [InlineData("remove draft for Phil Mathers and close the loop", GuardianCommandIntent.CancelDraftAndCloseLoop)]
    [InlineData("prepare me for my next meeting", GuardianCommandIntent.PrepareNextMeeting)]
    [InlineData("what happens next for Purchasing Review?", GuardianCommandIntent.ShowThreadNextActions)]
    [InlineData("what can move forward now?", GuardianCommandIntent.ShowThreadNextActions)]
    [InlineData("what is correctly waiting?", GuardianCommandIntent.ShowThreadNextActions)]
    [InlineData("what will you handle next?", GuardianCommandIntent.ShowScheduledInterventions)]
    [InlineData("what are you reconsidering today?", GuardianCommandIntent.ShowScheduledInterventions)]
    [InlineData("what have you scheduled on my behalf?", GuardianCommandIntent.ShowScheduledInterventions)]
    [InlineData("why will Guardian act on this?", GuardianCommandIntent.ShowScheduledInterventions)]
    [InlineData("how is Guardian performing?", GuardianCommandIntent.ShowPredictionCalibration)]
    [InlineData("how accurate are your predictions?", GuardianCommandIntent.ShowPredictionCalibration)]
    [InlineData("what did you get wrong?", GuardianCommandIntent.ShowPredictionCalibration)]
    [InlineData("how much admin time have you saved?", GuardianCommandIntent.ShowPredictionCalibration)]
    public void Parse_RecognisesLocalCommands(string input, GuardianCommandIntent intent) =>
        Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Fact]
    public void Parse_LeavesAmbiguousLanguageForGuardian() =>
        Assert.Equal(GuardianCommandIntent.Unknown, GuardianCommandParser.Parse("help me untangle pricing").Intent);

    [Fact]
    public void Resolve_MapsAbbreviatedTitleToCanonicalEntityAndId()
    {
        var entities = new[]
        {
            new GuardianEntity(GuardianEntityType.Task, "123", "YoY Spend Deep Dive"),
            new GuardianEntity(GuardianEntityType.Project, "45", "Category Review")
        };
        var result = GuardianEntityResolver.Resolve("yoy spend", entities, GuardianEntityType.Task);
        Assert.True(result.IsResolved);
        Assert.Equal("123", result.Entity!.Id);
        Assert.Equal("YoY Spend Deep Dive", result.Entity.Name);
    }

    [Fact]
    public void Resolve_DoesNotGuessBetweenEquallyStrongEntities()
    {
        var entities = new[]
        {
            new GuardianEntity(GuardianEntityType.Task, "1", "Pricing Review North"),
            new GuardianEntity(GuardianEntityType.Task, "2", "Pricing Review South")
        };
        var result = GuardianEntityResolver.Resolve("pricing review", entities);
        Assert.True(result.IsAmbiguous);
        Assert.False(result.IsResolved);
    }

    [Theory]
    [InlineData("remember that Sarah prefers calls over email", "Sarah prefers calls over email")]
    [InlineData("Guardian, remember: the design licence renews in March", "the design licence renews in March")]
    [InlineData("please remember Mark is off on Fridays", "Mark is off on Fridays")]
    public void Parse_RememberFact_CapturesFact(string input, string fact)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.RememberFact, result.Intent);
        Assert.Equal(fact, result.Argument);
    }

    [Fact]
    public void Parse_RememberTo_IsNotAFact() =>
        Assert.NotEqual(GuardianCommandIntent.RememberFact, GuardianCommandParser.Parse("remember to call Sarah").Intent);

    [Fact]
    public void Parse_ForgetFact_CapturesTopic()
    {
        var result = GuardianCommandParser.Parse("forget that Sarah prefers calls");
        Assert.Equal(GuardianCommandIntent.ForgetFact, result.Intent);
        Assert.Equal("Sarah prefers calls", result.Argument);
    }

    [Theory]
    [InlineData("forget it")]
    [InlineData("forget that")]
    public void Parse_ForgetIt_IsNotAFactCommand(string input) =>
        Assert.NotEqual(GuardianCommandIntent.ForgetFact, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("what do you remember")]
    [InlineData("what have I asked you to remember?")]
    public void Parse_ListFacts(string input) =>
        Assert.Equal(GuardianCommandIntent.ListFacts, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("reply to Sarah's email saying I'll look at it Thursday", "Sarah", "I'll look at it Thursday")]
    [InlineData("reply to Mark Jones's last email: thanks, approved.", "Mark Jones", "thanks, approved")]
    [InlineData("Can you reply to Sam Wells and let her know that she'll have her data tomorrow?", "Sam Wells", "she'll have her data tomorrow")]
    [InlineData("could you respond to Tom and tell him the licence is renewed", "Tom", "the licence is renewed")]
    [InlineData("reply to the last email from Priya saying yes, Friday works", "Priya", "yes, Friday works")]
    [InlineData("please reply to Sam’s email, I'll call her at 3", "Sam", "I'll call her at 3")]
    public void Parse_DraftEmailReply_CapturesSenderAndGist(string input, string who, string gist)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.DraftEmailReply, result.Intent);
        Assert.Equal(who, result.Argument);
        Assert.Equal(gist, result.Detail);
    }

    [Theory]
    [InlineData("brief me on Sam Wells", "Sam Wells")]
    [InlineData("who is Priya?", "Priya")]
    [InlineData("catch me up on Tom", "Tom")]
    [InlineData("what's going on with Sarah Jones?", "Sarah Jones")]
    [InlineData("anything new from Mark", "Mark")]
    public void Parse_PersonBrief(string input, string who)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.PersonBrief, result.Intent);
        Assert.Equal(who, result.Argument);
    }

    [Fact]
    public void Parse_BriefMe_StaysTheMorningBriefing() =>
        Assert.Equal(GuardianCommandIntent.BriefMe, GuardianCommandParser.Parse("brief me").Intent);

    [Theory]
    [InlineData("What did I promise today?", GuardianCommandIntent.PromisesToday)]
    [InlineData("what have I committed to today", GuardianCommandIntent.PromisesToday)]
    [InlineData("Read me my important emails", GuardianCommandIntent.ReadImportantEmails)]
    [InlineData("anything important in my inbox?", GuardianCommandIntent.ReadImportantEmails)]
    [InlineData("check my email", GuardianCommandIntent.ReadImportantEmails)]
    public void Parse_InboxVoiceCommands(string input, GuardianCommandIntent intent) =>
        Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Fact]
    public void Parse_PromisedThisWeek_Unchanged() =>
        Assert.Equal(GuardianCommandIntent.ShowPromisedThisWeek, GuardianCommandParser.Parse("what have i promised this week").Intent);

    [Theory]
    [InlineData("start it", GuardianCommandIntent.StartCurrentBlock, "")]
    [InlineData("Push it.", GuardianCommandIntent.PushCurrentBlock, "")]
    [InlineData("leave my calendar alone", GuardianCommandIntent.AutopilotToggle, "off")]
    [InlineData("autopilot on", GuardianCommandIntent.AutopilotToggle, "on")]
    [InlineData("Am I on track?", GuardianCommandIntent.PlanStatus, "")]
    [InlineData("how’s my day going", GuardianCommandIntent.PlanStatus, "")]
    public void Parse_AutopilotCommands(string input, GuardianCommandIntent intent, string argument)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(argument, result.Argument);
    }

    [Fact]
    public void Parse_StartNamedTask_StillStartsMission() =>
        Assert.Equal(GuardianCommandIntent.StartMission, GuardianCommandParser.Parse("start pricing review").Intent);

    [Theory]
    [InlineData("What am I not doing?")]
    [InlineData("what's slipping")]
    [InlineData("what have I been putting off")]
    public void Parse_ShowNeglect(string input) =>
        Assert.Equal(GuardianCommandIntent.ShowNeglect, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("plan my day")]
    [InlineData("Plan the rest of the day.")]
    [InlineData("sort my day out")]
    public void Parse_PlanMyDay(string input) =>
        Assert.Equal(GuardianCommandIntent.PlanMyDay, GuardianCommandParser.Parse(input).Intent);

    [Fact]
    public void Parse_PlanTask_StillSchedulesIt() =>
        Assert.Equal(GuardianCommandIntent.ScheduleEntity, GuardianCommandParser.Parse("plan pricing review").Intent);

    [Theory]
    [InlineData("do it", GuardianCommandIntent.ConfirmPending)]
    [InlineData("Go ahead.", GuardianCommandIntent.ConfirmPending)]
    [InlineData("add those tasks", GuardianCommandIntent.ConfirmPending)]
    [InlineData("what can you do on your own?", GuardianCommandIntent.AutonomySettings)]
    [InlineData("autonomy settings", GuardianCommandIntent.AutonomySettings)]
    public void Parse_AutonomyCommands(string input, GuardianCommandIntent intent) =>
        Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("give the licence renewal to Tom by Friday", "Tom", "the licence renewal|Friday")]
    [InlineData("Delegate the layout pilot report to Sarah Jones", "Sarah Jones", "the layout pilot report|")]
    [InlineData("can you hand over the supplier review to Priya, by end of the week", "Priya", "the supplier review|end of the week")]
    [InlineData("pass the backup checks over to mark.lee@example.com by tomorrow", "mark.lee@example.com", "the backup checks|tomorrow")]
    public void Parse_DelegateTask(string input, string who, string detail)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.DelegateTask, result.Intent);
        Assert.Equal(who, result.Argument);
        Assert.Equal(detail, result.Detail);
    }

    [Theory]
    [InlineData("that's not a promise", "promise", "")]
    [InlineData("No, that wasn't a promise.", "promise", "")]
    [InlineData("Alex's isn't a promise", "promise", "alex")]
    [InlineData("the Alex one is not a promise", "promise", "alex")]
    [InlineData("not a promise", "promise", "")]
    [InlineData("that project's fine", "project", "")]
    [InlineData("those projects are fine", "project", "")]
    [InlineData("the layout project is still live", "project", "layout")]
    [InlineData("that's done", "done", "")]
    [InlineData("I already did that", "done", "")]
    public void Parse_NeglectCorrection(string input, string kind, string target)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.NeglectCorrection, result.Intent);
        Assert.Equal(kind, result.Argument);
        Assert.Equal(target, result.Detail);
    }

    [Theory]
    [InlineData("that's a promise")]
    [InlineData("that's fine")]
    [InlineData("done")]
    public void Parse_NotANeglectCorrection(string input) =>
        Assert.NotEqual(GuardianCommandIntent.NeglectCorrection, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("hand off", GuardianCommandIntent.Handoff)]
    [InlineData("What does tomorrow look like?", GuardianCommandIntent.Handoff)]
    public void Parse_HandoffAndReport(string input, GuardianCommandIntent intent) =>
        Assert.Equal(intent, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("Catch me up on Teams", GuardianCommandIntent.TeamsCatchUp, "", "")]
    [InlineData("what did I miss on teams?", GuardianCommandIntent.TeamsCatchUp, "", "")]
    [InlineData("anything from my manager?", GuardianCommandIntent.TeamsFrom, "my manager", "")]
    [InlineData("Anything from Phil on Teams", GuardianCommandIntent.TeamsFrom, "phil", "")]
    [InlineData("what has Sam said?", GuardianCommandIntent.TeamsFrom, "sam", "")]
    [InlineData("treat Phil Jones as a manager", GuardianCommandIntent.TeamsManager, "phil jones", "add")]
    [InlineData("Sarah is my manager", GuardianCommandIntent.TeamsManager, "sarah", "add")]
    [InlineData("Kev isn't a manager", GuardianCommandIntent.TeamsManager, "kev", "remove")]
    [InlineData("who is my manager?", GuardianCommandIntent.TeamsManager, "", "list")]
    [InlineData("What did I miss?", GuardianCommandIntent.WhatDidIMiss, "", "")]
    [InlineData("catch me up", GuardianCommandIntent.WhatDidIMiss, "", "")]
    [InlineData("open source 2", GuardianCommandIntent.OpenHistorySource, "2", "")]
    [InlineData("review my invites", GuardianCommandIntent.ReviewInvites, "", "")]
    [InlineData("are you working properly?", GuardianCommandIntent.SelfCheck, "", "")]
    [InlineData("what needs approving?", GuardianCommandIntent.ApprovalInbox, "", "")]
    [InlineData("approve it", GuardianCommandIntent.ApproveIt, "", "")]
    [InlineData("decline it because we already have spare stock", GuardianCommandIntent.DeclineIt, "we already have spare stock", "")]
    [InlineData("project status website rollout", GuardianCommandIntent.ProjectStatus, "website rollout", "")]
    [InlineData("how's the firewall replacement project going", GuardianCommandIntent.ProjectStatus, "firewall replacement", "")]
    [InlineData("draft the project update", GuardianCommandIntent.ProjectUpdate, "", "")]
    [InlineData("what can you do with this?", GuardianCommandIntent.Capabilities, "this", "")]
    [InlineData("what can you do while I'm away", GuardianCommandIntent.Capabilities, "i'm away", "")]
    [InlineData("what can I ask you about Teams", GuardianCommandIntent.Capabilities, "teams", "")]
    public void Parse_Teams(string input, GuardianCommandIntent intent, string who, string detail)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(intent, result.Intent);
        Assert.Equal(who, result.Argument.ToLowerInvariant());
        Assert.Equal(detail, result.Detail);
    }

    [Theory]
    [InlineData("reply to this")]
    [InlineData("Can you log this as a ticket?")]
    [InlineData("what's this error")]
    [InlineData("who is this")]
    [InlineData("make this a task")]
    public void Parse_ScreenThis(string input) =>
        Assert.Equal(GuardianCommandIntent.ScreenThis, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("what's on this week")]
    [InlineData("what have I got this afternoon")]
    [InlineData("that's not a promise")]
    public void Parse_NotScreenThis(string input) =>
        Assert.NotEqual(GuardianCommandIntent.ScreenThis, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("when did we last change the firewall rules?")]
    [InlineData("what did we agree with Sophos about the renewal")]
    [InlineData("has anyone had this Outlook error before")]
    [InlineData("who fixed the VPN last time")]
    [InlineData("search for the Mimecast quote")]
    public void Parse_AskHistory(string input) =>
        Assert.Equal(GuardianCommandIntent.AskHistory, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("find me the next thing to do")]
    [InlineData("what should i do next")]
    public void Parse_NotAskHistory(string input) =>
        Assert.NotEqual(GuardianCommandIntent.AskHistory, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("anything from the suppliers")]
    [InlineData("what's Craig working on")]
    public void Parse_NotTeamsFrom(string input) =>
        Assert.NotEqual(GuardianCommandIntent.TeamsFrom, GuardianCommandParser.Parse(input).Intent);

    [Theory]
    [InlineData("let me interrupt you", "on")]
    [InlineData("stop listening while you talk", "off")]
    public void Parse_BargeInToggle(string input, string state)
    {
        var result = GuardianCommandParser.Parse(input);
        Assert.Equal(GuardianCommandIntent.BargeInToggle, result.Intent);
        Assert.Equal(state, result.Argument);
    }

    [Theory]
    [InlineData("give me the briefing")]
    [InlineData("reply to Sam's email saying I'll give it to Tom")]
    public void Parse_NotADelegation(string input) =>
        Assert.NotEqual(GuardianCommandIntent.DelegateTask, GuardianCommandParser.Parse(input).Intent);
}
