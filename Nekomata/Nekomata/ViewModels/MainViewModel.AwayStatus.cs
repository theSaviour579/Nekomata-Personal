using Microsoft.Extensions.DependencyInjection;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Authentication;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private bool _awayStatusSending;
    private DateTimeOffset? _lastAwayStatusAttempt;

    private async Task EvaluateScheduledAwayStatusAsync()
    {
        var now = DateTimeOffset.Now;
        if (_awayStatusSending || now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
            now.Hour != 16 || now.Minute < 25 ||
            (_lastAwayStatusAttempt is not null && now - _lastAwayStatusAttempt < TimeSpan.FromMinutes(15)))
            return;

        var start = new DateTimeOffset(now.Date, TimeZoneInfo.Local.GetUtcOffset(now.Date));
        var events = await _services.GetRequiredService<IWorkdayEventRepository>().GetBetweenAsync(start, start.AddDays(1), 500);
        if (events.Any(item => item.EventType == "AwayStatusSent")) return;
        await SendAwayStatusToSelfAsync(manual: false);
    }

    private async Task SendAwayStatusToSelfAsync(bool manual)
    {
        if (_awayStatusSending) return;
        _awayStatusSending = true;
        _lastAwayStatusAttempt = DateTimeOffset.Now;
        try
        {
            var authentication = _services.GetRequiredService<IMicrosoftAuthenticationService>();
            var recipient = (await authentication.GetTokenAsync()).AccountName;
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("Connect your Microsoft work account before sending an away status.");

            var now = DateTimeOffset.Now;
            var start = new DateTimeOffset(now.Date, TimeZoneInfo.Local.GetUtcOffset(now.Date));
            var events = await _services.GetRequiredService<IWorkdayEventRepository>().GetBetweenAsync(start, start.AddDays(1), 500);
            var completed = events.Where(item => item.EventType == "ObjectiveCompleted").OrderBy(item => item.OccurredAt).ToList();
            var current = MissionActive ? Workspace.CurrentMission.Title : "No mission currently active";
            var next = Workspace.RankedMissionCandidates.FirstOrDefault(item => item.TaskId != Workspace.CurrentMission.TaskId)?.Title
                       ?? "No next objective selected";
            var openCommitments = Workspace.Tasks.Count(item => !item.IsCompleted);
            var dueTomorrow = Workspace.Tasks.Count(item => !item.IsCompleted && item.DueAt?.Date == DateTime.Today.AddDays(1));
            var body = $"""
                Guardian status — {now:dddd dd MMMM yyyy HH:mm}

                Current state
                {current}

                Next objective
                {next}

                Completed today ({completed.Count})
                {(completed.Count == 0 ? "None recorded" : string.Join(Environment.NewLine, completed.Select(item => $"- {item.TimeLabel} {item.Narrative}")))}

                Interruptions
                {InterruptionSummary}

                Remaining
                {openCommitments} open commitments · {dueTomorrow} due tomorrow · {Workspace.Capacity.RemainingMinutesToday} minutes capacity remaining

                Sent automatically by Nekomata Guardian.
                """;
            await _services.GetRequiredService<IEmailService>().SendMessageAsync(
                recipient,
                $"Guardian status — {now:ddd dd MMM}",
                body);

            await RecordWorkdayEventAsync(new WorkdayEvent
            {
                EventType = "AwayStatusSent", Category = "Communication", Title = "Away status emailed",
                Narrative = $"Guardian emailed the {now:HH:mm} working-state summary to the signed-in account.",
                Outcome = "Sent", Significance = 45
            });
            GuardianResponse = $"Guardian status emailed to {recipient}.";
            if (manual) GuardianPanelExpanded = true;
        }
        catch (Exception ex)
        {
            GuardianResponse = $"Guardian could not email the status update: {ex.Message}";
            if (manual) GuardianPanelExpanded = true;
        }
        finally { _awayStatusSending = false; }
    }
}
