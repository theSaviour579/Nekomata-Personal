using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian;

namespace Nekomata.UI.ViewModels;

// Personality through judgement (advice only).
//   * "Anything I need to worry about?": a straight answer from what Guardian can see (GuardianJudgement). When nothing
//     needs the user it says so and recommends leaving things alone; when it can't see a source it says that instead.
public partial class MainViewModel
{
    private async Task AnythingToWorryAboutAsync()
    {
        var now = DateTimeOffset.Now;
        var local = now.LocalDateTime;
        var owed = ActiveCommitments.Where(c => IsRealPromise(c) && c.Direction == CommitmentDirection.OwedByMe && c.DueAt is not null).ToList();
        var overdueTasks = Workspace.Tasks
            .Where(t => !t.Completed && t.DueAt is { } due && due < local)
            .OrderBy(t => t.DueAt)
            .Select(t => $"'{Trim(t.Title, 50)}'")
            .ToList();
        var nextMeeting = CalendarEvents
            .Where(e => !e.IsAllDay && !e.IsCancelled && !e.IsNekomataManaged && e.Attendees.Count > 0 &&
                        e.Start > now && e.Start < now.AddHours(2))
            .OrderBy(e => e.Start)
            .FirstOrDefault();
        var facts = new SituationFacts
        {
            CalendarKnown = CalendarLoaded,
            NeedsYou = VisibleAttentionItems.Select(x => (x.Severity, x.Title)).ToList(),
            OverdueTasks = overdueTasks,
            OverduePromises = owed.Where(c => c.DueAt!.Value.LocalDateTime < local).Select(c => LowerFirst(c.Summary)).ToList(),
            PromisesToday = owed.Where(c => c.DueAt!.Value.LocalDateTime >= local && c.DueAt.Value.LocalDateTime.Date == local.Date).Select(c => LowerFirst(c.Summary)).ToList(),
            FailingJobs = JobHealthLines(),
            ApprovalsWaiting = Approvals.Count(a => a.Decision.Length == 0),
            ApprovalsOld = Approvals.Count(a => a.Decision.Length == 0 && a.At < now.AddDays(-1)),
            ManagersWaiting = TeamsUpdates.Where(u => u.FromManager && !u.Replied && !u.Dismissed).Select(u => FirstName(u.Sender)).Distinct().ToList(),
            MeetingsWithoutPrep = nextMeeting is null ? [] : [$"the {CleanCalendarObjectiveTitle(nextMeeting.Subject)} at {nextMeeting.Start.LocalDateTime:HH:mm}"]
        };
        var answer = GuardianJudgement.AnythingToWorryAbout(facts);
        await RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "AllClearAsked", Category = "Guardian", Title = answer.AllClear ? "Said nothing needed attention" : $"Raised {answer.Concerns.Count(c => c.Weight >= 2)} concerns",
            Narrative = Trim(answer.Spoken, 400), Outcome = answer.AllClear ? "AllClear" : "Concerns", Significance = 30
        });
        ShowPaletteResponse(answer.Written);
        await SpeakGuardianAsync(answer.Spoken, true);
    }

    private static string LowerFirst(string s) => s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;
}
