using System.IO;
using Nekomata.Core.Guardian.Commands;

namespace Nekomata.UI.ViewModels;

public sealed record GuardianCapability(string Area, string Say, string Does, GuardianCommandIntent Intent);

// "What can you do?" - answered for where the user is rather than as a help page. "What can you do with this?" looks at
// the window they were using and offers what fits it (an email: reply, task, remember...). "What can you do in Teams /
// while I'm away / with my calendar?" lists that area. Each answer also suggests one thing in that area they haven't
// used for a fortnight (from capability-usage.json, written whenever a command runs), so forgotten features
// resurface.
public partial class MainViewModel
{
    private static string CapabilityUsagePath => Path.Combine(DataFolder, "capability-usage.json");
    private Dictionary<string, DateTimeOffset>? _capabilityUsage;
    private Dictionary<string, DateTimeOffset> CapabilityUsage => _capabilityUsage ??= LoadJson<Dictionary<string, DateTimeOffset>>(CapabilityUsagePath) ?? [];

    private static readonly GuardianCapability[] Capabilities =
    [
        new("this", "reply to this", "drafts your reply to the email or chat in front of you", GuardianCommandIntent.ScreenThis),
        new("this", "what's this error?", "explains what's on screen and what to do about it", GuardianCommandIntent.ScreenThis),
        new("this", "who is this?", "briefs you on the person shown", GuardianCommandIntent.ScreenThis),
        new("this", "make this a task / remember this", "adds a task or keeps a fact", GuardianCommandIntent.ScreenThis),
        new("teams", "catch me up on Teams", "managers, people waiting on you and what's changed in your chats", GuardianCommandIntent.TeamsCatchUp),
        new("teams", "anything from my manager?", "everything your managers have said recently, and whether you've replied", GuardianCommandIntent.TeamsFrom),
        new("teams", "what has Sam said?", "one person's recent Teams messages", GuardianCommandIntent.TeamsFrom),
        new("teams", "treat Phil as a manager", "their messages alert you straight away (and to your phone when you're away)", GuardianCommandIntent.TeamsManager),
        new("email", "read me my important emails", "unread mail from people, without the noise", GuardianCommandIntent.ReadImportantEmails),
        new("email", "reply to Sam saying ...", "drafts a reply in your style into Outlook Drafts", GuardianCommandIntent.DraftEmailReply),
        new("email", "what did I promise today?", "the commitments in today's sent mail", GuardianCommandIntent.PromisesToday),
        new("calendar", "review my invites", "unanswered invitations with what to reply; do it sends the reply", GuardianCommandIntent.ReviewInvites),
        new("calendar", "project status website relaunch", "what moved, what's blocked, who it's waiting on, what's next", GuardianCommandIntent.ProjectStatus),
        new("calendar", "draft the project update", "the weekly update across all projects, into Outlook Drafts (also automatic on Friday afternoons)", GuardianCommandIntent.ProjectUpdate),
        new("email", "what needs approving?", "approval requests from email and Teams, with history; approve it / decline it drafts the reply", GuardianCommandIntent.ApprovalInbox),
        new("voice", "are you working properly?", "checks every connection with a real call; also runs each morning", GuardianCommandIntent.SelfCheck),
        new("calendar", "plan my day", "lays out focus time around your meetings", GuardianCommandIntent.PlanMyDay),
        new("calendar", "am I on track?", "where you are against today's plan", GuardianCommandIntent.PlanStatus),
        new("calendar", "hand off", "what got done, what moved and how tomorrow looks", GuardianCommandIntent.Handoff),
        new("away", "what did I miss?", "one catch-up since you last looked; it also runs after meetings", GuardianCommandIntent.WhatDidIMiss),
        new("away", "(automatic) welcome back", "a spoken catch-up when you unlock after ten minutes away", GuardianCommandIntent.Unknown),
        new("promises", "what do I owe?", "walks through your promises: done, drop, book, skip", GuardianCommandIntent.ShowIOwe),
        new("promises", "what's slipping?", "what you're not getting to, with what to do about each", GuardianCommandIntent.ShowNeglect),
        new("history", "when did we last ...?", "answers about the past from email, Teams and notes, with sources", GuardianCommandIntent.AskHistory),
        new("history", "remember that ...", "keeps a fact for every future conversation", GuardianCommandIntent.RememberFact),
        new("voice", "Ctrl+Alt+Space", "talk to Guardian; it looks at the window you're in, and the conversation carries on until you stop", GuardianCommandIntent.Unknown),
        new("voice", "quiet for an hour", "holds anything spoken until then", GuardianCommandIntent.QuietMode),
        new("voice", "play music", "Spotify: play, pause, next song, volume to 30, what's playing, \"play Coldplay\", focus music", GuardianCommandIntent.Music),
        new("operations", "tell me when Sam replies", "I keep an eye on it and tell you: email replies and Teams messages", GuardianCommandIntent.AddWatch),
        new("operations", "what are you watching?", "lists what I'm keeping an eye on; \"stop watching …\" cancels one", GuardianCommandIntent.ListWatches),
        new("operations", "end of day protocol", "runs a named routine: morning, end of day, monday, friday, focus, or your own", GuardianCommandIntent.RunProtocol),
        new("operations", "what protocols do you have?", "lists the protocols; make your own with \"create protocol lunch: plan my day; anything I need to worry about\"", GuardianCommandIntent.ListProtocols),
        new("operations", "systems check", "checks every connection and says whether all systems are online", GuardianCommandIntent.SelfCheck),
        new("operations", "anything I need to worry about?", "a straight answer: what needs you and what can wait, or that nothing does and you can leave it", GuardianCommandIntent.AnythingToWorry),
        new("history", "what have you sent to the AI?", "today's AI calls: how many, by feature, and how many names, addresses and amounts were swapped for placeholders", GuardianCommandIntent.AiPrivacyReport),
        new("history", "how is Guardian doing?", "how my judgement held up this week: useful vs dismissed, what I missed, what you corrected", GuardianCommandIntent.Scorecard),
        new("history", "you should have caught that: …", "tells me what I missed, so I learn what matters", GuardianCommandIntent.MissedIssue),
        new("operations", "is Guardian OK?", "every background job: when it last worked, and anything that's stopped", GuardianCommandIntent.JobHealth),
        new("planner", "week in review", "Friday's review of the week drafted in Outlook (never sent)", GuardianCommandIntent.WeekReview),
        new("voice", "Ctrl+Alt+N", "a note box over whatever you're in; Enter files it like a note to yourself in Teams", GuardianCommandIntent.Unknown),
        new("history", "that was wrong: …", "marks my last answer down and keeps the correction; 👍 and 👎 on any answer teach me too", GuardianCommandIntent.Correction),
        new("history", "how am I rating you?", "your 👍 and 👎 over the week, what keeps missing and what I've learned", GuardianCommandIntent.FeedbackSummary),
        new("planner", "setup check", "what's set up and what's left, each with what to do", GuardianCommandIntent.SetupCheck),
        new("planner", "G: what's on tomorrow?", "ask from your phone (Teams self-chat, To Do or email) and the answer comes back", GuardianCommandIntent.Unknown),
        new("planner", "morning brief", "the start-of-day brief again: weather, what came in, your day, what's due, where to start", GuardianCommandIntent.MorningBrief),
        new("planner", "what's due?", "recurring duties: weekly and monthly reviews, or anything you add in the Planner", GuardianCommandIntent.WhatsDue),
        new("planner", "add objective: …", "your objectives, with finished work linked to them each afternoon", GuardianCommandIntent.AddObjective),
        new("planner", "objective evidence", "everything linked to your objectives, in Word for your appraisal", GuardianCommandIntent.ObjectiveEvidence),
        new("planner", "where did my time go?", "today by kind of work; \"time this week\" for the week and how reactive it was", GuardianCommandIntent.TimeToday),
        new("planner", "review my recurring meetings", "what to drop, shorten or make fortnightly, with messages drafted", GuardianCommandIntent.MeetingReview),
        new("planner", "I'm in the office", "learns where you are; \"when I'm next in: …\" reminds you on arrival", GuardianCommandIntent.Place),
        new("planner", "G: … (from Teams or email)", "message yourself starting G: and it becomes a task, reminder or note", GuardianCommandIntent.Unknown),
        new("planner", "planner", "duties, objectives and time in one window", GuardianCommandIntent.Planner),
        new("voice", "what's the weather?", "conditions and the outlook; warns you if it'll rain at home time", GuardianCommandIntent.Weather),
        new("voice", "settings", "hours, background features, managers, thresholds, weather and Microsoft accounts in one page", GuardianCommandIntent.GuardianSettings),
        new("voice", "I'm working / off duty", "out of hours I only interrupt for something critical or your manager; \"I'm working\" lifts that for four hours", GuardianCommandIntent.QuietMode),
    ];

    private void RecordCapabilityUse(GuardianCommandIntent intent)
    {
        if (intent == GuardianCommandIntent.Unknown || intent == GuardianCommandIntent.Capabilities) return;
        CapabilityUsage[intent.ToString()] = DateTimeOffset.Now;
        SaveJson(CapabilityUsagePath, CapabilityUsage);
    }

    private static string? CapabilityArea(string topic)
    {
        topic = topic.ToLowerInvariant();
        if (topic.Length == 0) return null;
        if (topic.Contains("this") || topic.Contains("screen")) return "this";
        if (topic.Contains("team")) return "teams";
        if (topic.Contains("mail") || topic.Contains("outlook") || topic.Contains("inbox")) return "email";
        if (topic.Contains("calendar") || topic.Contains("meeting") || topic.Contains("invite") || topic.Contains("day")) return "calendar";
        if (topic.Contains("away") || topic.Contains("out") || topic.Contains("phone")) return "away";
        if (topic.Contains("promise") || topic.Contains("owe")) return "promises";
        if (topic.Contains("history") || topic.Contains("past") || topic.Contains("remember")) return "history";
        if (topic.Contains("voice") || topic.Contains("talk")) return "voice";
        return null;
    }

    private async Task ShowCapabilitiesAsync(string topic)
    {
        var area = CapabilityArea(topic);
        string heading;
        List<GuardianCapability> list;
        if (area == "this")
        {
            var snap = CurrentScreenSnapshot();
            var app = (snap?.App ?? "").ToLowerInvariant();
            var title = snap?.Title ?? "";
            var kind = app.Contains("outlook") || app.Contains("olk") ? "email"
                : app.Contains("teams") ? "Teams chat"
                : snap is null ? "" : "window";
            heading = kind.Length == 0 ? "WITH \"THIS\" (I can't see a window right now: click into one, then ask)" : $"WITH THIS {kind.ToUpperInvariant()} · {title}";
            list = Capabilities.Where(c => c.Area == "this").ToList();
        }
        else if (area is not null)
        {
            heading = area switch
            {
                "teams" => "IN TEAMS", "email" => "WITH EMAIL", "calendar" => "WITH YOUR CALENDAR",
                "away" => "WHILE YOU'RE AWAY", "promises" => "WITH PROMISES", "history" => "WITH HISTORY", _ => "BY VOICE"
            };
            list = Capabilities.Where(c => c.Area == area).ToList();
        }
        else
        {
            heading = "A FEW THINGS TO TRY · ask \"what can you do in Teams / with this / while I'm away?\"";
            list = Capabilities.GroupBy(c => c.Area).Select(g => g.First()).ToList();
        }

        var forgotten = list.Where(c => c.Intent != GuardianCommandIntent.Unknown &&
                                        (!CapabilityUsage.TryGetValue(c.Intent.ToString(), out var used) || DateTimeOffset.Now - used > TimeSpan.FromDays(14)))
            .Select(c => c).FirstOrDefault();
        ShowPaletteResponse($"WHAT I CAN DO {heading}\n\n" + string.Join("\n", list.Select(c => $"• \"{c.Say}\" - {c.Does}")) +
                            (forgotten is null ? "" : $"\n\nWorth a try: \"{forgotten.Say}\""));
        var spokenList = list.Where(c => !c.Say.StartsWith('(')).Take(3).Select(c => c.Say.Split(" / ")[0]).ToList();
        await SpeakGuardianAsync($"For example: {JoinNames(spokenList)}. The rest are on screen." +
                                 (forgotten is null ? "" : $" You haven't tried {forgotten.Say.TrimEnd('?')} lately."), true);
    }
}
