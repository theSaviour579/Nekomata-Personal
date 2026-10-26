using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Evening wrap-up as a conversation. After the automatic end-of-day review is spoken,
// Guardian asks "Want me to plan tomorrow?" and listens. Yes runs the same plan-tomorrow
// request as the review's button, then the normal voice-confirmation flow reads back the
// proposed calendar blocks and books them on a second yes. No ends politely; anything
// else is handled as an ordinary conversation turn; silence leaves the review on screen.
public partial class MainViewModel
{
    // "Wrap up my day": the same spoken review as the automatic 16:15 one, on demand. Inside a voice
    // conversation the offer to plan tomorrow is skipped, because the conversation itself carries on and
    // "plan tomorrow" can simply be said next.
    private async Task WrapUpDayOnRequestAsync()
    {
        var promises = StartPromisesForWrapUp();
        await ShowDayReviewAsync();
        if (!DayReviewVisible) return;
        await SpeakEndOfDayReviewAsync();
        await RemindOfPromisesAsync(promises);
        if (!_conversationActive) await OfferToPlanTomorrowAsync();
    }

    // The sent-mail analysis takes a few seconds, so it starts alongside the review rather than after it.
    private Task<PromisesSummary?> StartPromisesForWrapUp() => Task.Run(async () =>
    {
        try { return (PromisesSummary?)await ComposePromisesTodayAsync(TimeSpan.FromMinutes(30)); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Wrap-up promises unavailable: {ex.Message}");
            return null;
        }
    });

    // "Before you go: you told Sam you'd send the data tomorrow." Only speaks when there is something to say.
    private async Task RemindOfPromisesAsync(Task<PromisesSummary?> pending)
    {
        try
        {
            var summary = await pending.WaitAsync(TimeSpan.FromSeconds(40));
            if (summary is not { PromiseCount: > 0 }) return;
            var screen = "From today's sent emails:" + Environment.NewLine +
                         string.Join(Environment.NewLine, summary.Lines.Select(l => "- " + l));
            ShowPaletteResponse(screen);
            var spoken = summary.PromiseCount == 1
                ? $"Before you go, one promise from today's emails: {summary.Lines[0]}"
                : $"Before you go, you made {summary.PromiseCount} promises by email today. " +
                  string.Join(" ", summary.Lines.Take(4).Select(l => l.TrimEnd('.') + ".")) +
                  (summary.PromiseCount > 4 ? " The rest are on screen." : "");
            await SpeakAndSettleAsync(spoken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Wrap-up promise reminder failed safely: {ex.Message}");
        }
    }

    private async Task OfferToPlanTomorrowAsync()
    {
        if (!GuardianVoiceEnabled || _conversationActive || GuardianBusy || !DayReviewVisible) return;
        try
        {
            await WaitForAsync(() => _guardianSpeakingDepth == 0, TurnTimeout);
            _pendingVoiceOffer = new(() =>
            {
                PlanTomorrowFromReview();
                var request = ChatInput;
                ChatInput = "";
                return Task.FromResult(request);
            }, "Okay. Have a good evening.", DateTimeOffset.Now + VoiceOfferWindow);
            await SpeakAndSettleAsync("Want me to plan tomorrow? Press the talk key and say yes.");
            Think("Offered to plan tomorrow · talk key + yes");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Evening wrap-up offer failed safely: {ex.Message}");
        }
    }
}
