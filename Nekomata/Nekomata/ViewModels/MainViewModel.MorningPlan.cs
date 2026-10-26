using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Commitments;
using Nekomata.Core.Guardian.Neglect;
using Nekomata.Models.Planning;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// The morning plan you agree to. After the briefing, if today has no Guardian focus blocks yet, Guardian asks "Want me
// to lay out today?". Yes sends a plan-today request - free time around meetings, the highest-value open work, time for
// promises due soon and the worst of what's slipping - and the normal voice-confirmation flow reads the blocks back and
// books them on a second yes. From then on Day Autopilot keeps them workable and keeps the user on them.
// "Plan my day" does the same on demand (typed or spoken), without the opening question.
public partial class MainViewModel
{
    private async Task OfferToPlanTodayAsync()
    {
        try
        {
            if (!_autonomy.IsOn(Nekomata.Core.Guardian.Autonomy.AutonomyAction.MorningPlanOffer)) return;
            if (!GuardianVoiceEnabled || _conversationActive || GuardianBusy || MissionActive) return;
            var now = DateTimeOffset.Now;
            var end = new DateTimeOffset(_services.GetRequiredService<WorkingDaySettings>().GetEnd(DateTime.Today));
            if (end - now < TimeSpan.FromHours(1)) return;
            _autopilotToday = await LoadAutopilotDayAsync(now);
            if (_autopilotToday.Any(e => e.IsNekomataManaged && !e.IsAllDay && e.End > now)) return; // already planned

            await WaitForAsync(() => _guardianSpeakingDepth == 0, TurnTimeout);
            _pendingVoiceOffer = new(BuildPlanTodayRequestAsync, "Okay. Say plan my day whenever you want one.", DateTimeOffset.Now + VoiceOfferWindow);
            await SpeakAndSettleAsync("You've no focus time booked today. Want me to lay out your day? Press the talk key and say yes.");
            Think("Offered to lay out the day · talk key + yes");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Morning plan offer failed safely: {ex.Message}");
        }
    }

    private async Task PlanMyDayAsync()
    {
        ChatInput = await BuildPlanTodayRequestAsync();
        GuardianPanelExpanded = true;
        await SendGuardianMessageAsync();
    }

    private async Task<string> BuildPlanTodayRequestAsync()
    {
        var request = "Plan the rest of today. Create Guardian focus blocks only in my free time between now and the end of my " +
                      "working day, around my existing meetings, which must not move. Put the highest-value open work first, in the " +
                      "strongest focus windows, sized by predicted calendar requirement rather than the raw estimate. Leave breathing " +
                      "room after meetings.";
        if (_autopilotProfile.PreferredWindowLabel.Length > 0)
            request += $" My best focus time is {_autopilotProfile.PreferredWindowLabel}: put the hardest work there if it's free.";
        if (_autopilotProfile.AvoidHours.Count > 0)
            request += " Avoid booking focus work starting in these hours: " + string.Join(", ", _autopilotProfile.AvoidHours.Select(h => $"{h:00}:00")) + ".";
        try
        {
            var due = ActiveCommitments
                .Where(c => IsRealPromise(c) && c.DueAt is { } d && d - DateTimeOffset.Now <= TimeSpan.FromDays(2) && DateTimeOffset.Now - d <= TimeSpan.FromDays(7))
                .OrderBy(c => c.DueAt).Take(5).Select(c => c.Summary).ToList();
            if (due.Count > 0) request += " Make time for these promises that are due soon: " + string.Join("; ", due) + ".";

            var slipping = (await DetectNeglectAsync())
                .Where(i => i.Kind is NeglectKind.SlippingTask or NeglectKind.AvoidedWork && i.TaskId is not null)
                .Take(3).Select(i => i.Title).ToList();
            if (slipping.Count > 0) request += " These keep slipping, so include them if there's room: " + string.Join("; ", slipping) + ".";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plan-today context unavailable: {ex.Message}");
        }
        return request;
    }
}
