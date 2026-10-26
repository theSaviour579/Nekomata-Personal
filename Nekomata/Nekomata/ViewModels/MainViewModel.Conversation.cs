using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.Services;
using System.Text.RegularExpressions;

namespace Nekomata.UI.ViewModels;

// Conversation mode. Ctrl+Alt+Space starts a spoken exchange rather than a single
// command: after Guardian answers aloud, the microphone reopens for a few seconds so a
// follow-up can simply be said. Silence, "thanks"/"that's all", the turn limit, or
// turning voice off ends it.
//
// The hotkey means different things depending on what Guardian is doing:
//   listening → stop recording now and send what was heard (as before);
//   speaking  → cut Guardian off and listen straight away (barge-in);
//   idle      → start a new conversation.
// The microphone is never open while Guardian is speaking, so its own voice can't be
// picked up as a reply.
public partial class MainViewModel
{
    private const int MaxConversationTurns = 6;
    private static readonly TimeSpan FollowUpWindow = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(90);
    private const int SpokenReplyLimit = 320;

    private static readonly Regex ConversationEnd = new(
        @"^(no( thanks| thank you)?|nope|thanks?( you)?( very much| guardian)?|cheers|that'?s (all|it|everything|fine)|all good|stop|cancel|never ?mind|nothing( else)?|goodbye|bye|done)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private CancellationTokenSource? _listenCts;

    // A question Guardian asked on its own ("want me to plan tomorrow?"). The mic isn't opened for it: the user answers by
    // pressing the talk key within a few minutes, and a yes turns into the request.
    private sealed record PendingVoiceOffer(Func<Task<string>> Accept, string Declined, DateTimeOffset Until);
    private PendingVoiceOffer? _pendingVoiceOffer;
    private static readonly TimeSpan VoiceOfferWindow = TimeSpan.FromMinutes(5);
    private bool _conversationActive;
    private int _guardianUtteranceCount;

    [RelayCommand]
    private async Task TalkToGuardianAsync()
    {
        // The talk key during a protocol stops it.
        if (_protocolRunning)
        {
            _protocolStop = true;
            _guardianSpeech.StopSpeaking();
            ShowPaletteResponse("Protocol stopped.");
            return;
        }
        if (_listenCts is not null)
        {
            _listenCts.Cancel();
            return;
        }
        if (_conversationActive)
        {
            if (_guardianSpeakingDepth > 0) _guardianSpeech.StopSpeaking();
            return;
        }
        if (GuardianBusy) return;

        var listening = _services.GetRequiredService<GuardianListeningService>();
        if (!listening.NeuralListeningAvailable)
        {
            GuardianResponse = "Talking to Guardian needs an OpenAI API key configured first; use the command palette's microphone for offline dictation instead.";
            GuardianPanelExpanded = true;
            return;
        }

        _conversationActive = true;
        try
        {
            var heard = await ListenOnceAsync(listening, followUp: false);
            if (_pendingVoiceOffer is { } offer)
            {
                _pendingVoiceOffer = null;
                if (DateTimeOffset.Now < offer.Until && !string.IsNullOrWhiteSpace(heard))
                {
                    var answer = NormaliseUtterance(heard);
                    if (Affirmative.IsMatch(answer)) heard = await offer.Accept();
                    else if (Negative.IsMatch(answer) || IsConversationEnd(heard))
                    {
                        await SpeakAndSettleAsync(offer.Declined);
                        return;
                    }
                }
            }
            await RunConversationLoopAsync(listening, heard);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Talk to Guardian failed: {ex.Message}");
        }
        finally
        {
            EndConversation();
        }
    }

    // The shared turn loop. Callers set _conversationActive first and call
    // EndConversation afterwards; firstUtterance is what to handle before listening again
    // (what the user said, or a request Guardian is acting on, such as planning tomorrow).
    private async Task RunConversationLoopAsync(GuardianListeningService listening, string? firstUtterance)
    {
        var heard = firstUtterance;
        for (var turn = 1; !string.IsNullOrWhiteSpace(heard); turn++)
        {
            // During a promise review "done" and "stop" are answers, not goodbyes.
            if (_promiseReview is null && IsConversationEnd(heard))
            {
                await SpeakGuardianAsync("Understood.", true);
                break;
            }

            await RunSpokenTurnAsync(heard);

            if (!GuardianVoiceEnabled || (turn >= MaxConversationTurns && _promiseReview is null)) break;
            if (_bargeInUtterance is { } interrupted)
            {
                // the user talked over the answer: what he said is the next turn.
                _bargeInUtterance = null;
                heard = await interrupted;
                continue;
            }
            heard = HasVoiceConfirmableProposal()
                ? await ConfirmProposalByVoiceAsync(listening)
                : await ListenOnceAsync(listening, followUp: true);
        }
    }

    private void EndConversation()
    {
        _conversationActive = false;
        if (GuardianPresenceState is GuardianPresenceState.Listening or GuardianPresenceState.Thinking)
            GuardianPresenceState = GuardianPresenceState.Idle;
    }

    private async Task<string?> ListenOnceAsync(GuardianListeningService listening, bool followUp)
    {
        // On a call, don't keep the mic open for follow-ups: his half of the call would be taken as commands.
        if (followUp && LiveCallReason() is not null) return null;
        _listenCts = new CancellationTokenSource();
        SpeechInputActive = true;
        GuardianPresenceState = GuardianPresenceState.Listening;
        try
        {
            return followUp
                ? await listening.ListenForFollowUpAsync(FollowUpWindow, _listenCts.Token)
                : await listening.ListenAndTranscribeAsync(_listenCts.Token);
        }
        finally
        {
            SpeechInputActive = false;
            if (GuardianPresenceState == GuardianPresenceState.Listening)
                GuardianPresenceState = GuardianPresenceState.Idle;
            _listenCts.Dispose();
            _listenCts = null;
        }
    }

    // Routes the spoken text through the same command dispatch the typed palette uses
    // (so "what's waiting on me" gets the real structured answer), waits for the turn to
    // finish, and makes sure something is said aloud: many palette answers only appear
    // on screen, which would leave a voice conversation hanging in silence.
    private async Task RunSpokenTurnAsync(string heard)
    {
        var utterancesBefore = _guardianUtteranceCount;
        var responseBefore = GuardianResponse;
        GuardianPresenceState = GuardianPresenceState.Thinking;

        CommandPaletteInput = heard;
        await ExecuteCommandPaletteAsync();
        await WaitForAsync(() => !GuardianBusy, TurnTimeout);

        if (_guardianUtteranceCount == utterancesBefore && GuardianVoiceEnabled)
        {
            var response = GuardianResponse;
            var spoken = string.IsNullOrWhiteSpace(response) || response == responseBefore
                ? "Done. It's on screen."
                : SpokenVersion(response);
            await SpeakGuardianAsync(spoken, true);
        }

        await WaitForAsync(() => _guardianSpeakingDepth == 0, TurnTimeout);
        if (GuardianPresenceState == GuardianPresenceState.Thinking)
            GuardianPresenceState = GuardianPresenceState.Idle;
        // A short beat so the end of Guardian's own audio isn't caught by the microphone.
        await Task.Delay(300);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(150);
    }

    private static bool IsConversationEnd(string heard) => ConversationEnd.IsMatch(NormaliseUtterance(heard));

    // Screen answers are often lists; read a short answer whole, otherwise the opening
    // line and point to the screen rather than reciting every bullet.
    private static string SpokenVersion(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('•', '-', '*', ' '))
            .Where(x => x.Length > 0)
            .ToList();
        if (lines.Count == 0) return "Done. It's on screen.";

        var whole = string.Join(". ", lines.Select(x => x.TrimEnd('.', ':')));
        if (whole.Length <= SpokenReplyLimit && lines.Count <= 4) return whole + ".";

        var first = lines[0].TrimEnd(':', '.');
        if (first.Length > SpokenReplyLimit) first = first[..SpokenReplyLimit].TrimEnd() + "…";
        return $"{first}. The full details are on screen.";
    }
}
