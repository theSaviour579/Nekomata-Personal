using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.Services;

namespace Nekomata.UI.ViewModels;

// Talking over Guardian. While the neural voice is playing, the microphone listens for the user starting to speak; when he
// does, Guardian stops mid-sentence and what he said becomes the next turn. In a conversation the loop picks it up;
// otherwise it starts one. Off with "stop listening while you talk", back on with "let me interrupt you"; remembered.
// It never runs during a phone call, or when only the Windows voice is available (that can't be told apart from him).
// It only listens during a conversation the user started with the talk key (Ctrl+Alt+Space or the Talk button); anything
// Guardian says on its own - alerts, briefings, nudges - never opens the microphone.
public partial class MainViewModel
{
    private bool? _bargeInEnabled;
    private CancellationTokenSource? _bargeInCts;
    private bool _bargeInRunning;
    private bool _bargedIn;
    private Task<string?>? _bargeInUtterance;

    private static string VoiceSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "voice-settings.json");

    private sealed class VoiceSettings { public bool BargeIn { get; set; } = true; }

    private bool BargeInEnabled
    {
        get
        {
            if (_bargeInEnabled is { } known) return known;
            try
            {
                if (File.Exists(VoiceSettingsPath))
                    return (_bargeInEnabled = JsonSerializer.Deserialize<VoiceSettings>(File.ReadAllText(VoiceSettingsPath))?.BargeIn ?? true).Value;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Voice settings unreadable: {ex.Message}"); }
            return (_bargeInEnabled = true).Value;
        }
    }

    private async Task SetBargeInAsync(bool on)
    {
        _bargeInEnabled = on;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VoiceSettingsPath)!);
            File.WriteAllText(VoiceSettingsPath, JsonSerializer.Serialize(new VoiceSettings { BargeIn = on }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Voice settings not saved: {ex.Message}"); }
        if (!on) _bargeInCts?.Cancel();
        await RespondAndSpeakAsync(on
            ? "Okay. Just start talking while I'm speaking and I'll stop and listen."
            : "Okay. I won't listen while I'm talking. The talk hotkey still cuts me off.");
    }

    private void StartBargeInMonitor()
    {
        // Only inside a conversation the user opened with the talk key: unprompted speech (alerts, briefings) never opens the mic.
        if (!_conversationActive) return;
        if (_bargeInRunning || !BargeInEnabled || !GuardianVoiceEnabled || _listenCts is not null || SpeechInputActive ||
            LiveCallReason() is not null || !_guardianSpeech.NeuralVoiceAvailable) return;
        var listening = _services.GetRequiredService<GuardianListeningService>();
        if (!listening.NeuralListeningAvailable) return;

        _bargeInRunning = true;
        _bargedIn = false;
        _bargeInCts = new CancellationTokenSource();
        Task<string?>? task = null;
        task = listening.ListenForBargeInAsync(
            () => _guardianSpeech.IsPlaying,
            () =>
            {
                _bargedIn = true;
                _guardianSpeech.StopSpeaking();
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (_conversationActive) _bargeInUtterance = task;
                    GuardianPresenceState = GuardianPresenceState.Listening;
                    Think("You're talking. I'm listening");
                });
            },
            _bargeInCts.Token);
        _ = FinishBargeInAsync(task, listening);
    }

    private void StopBargeInMonitor()
    {
        if (!_bargedIn) _bargeInCts?.Cancel();
    }

    private async Task FinishBargeInAsync(Task<string?> task, GuardianListeningService listening)
    {
        string? heard = null;
        try { heard = await task; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Barge-in failed safely: {ex.Message}"); }
        finally
        {
            _bargeInRunning = false;
            _bargeInCts?.Dispose();
            _bargeInCts = null;
        }
        if (!_bargedIn) return;
        _bargedIn = false;
        if (string.IsNullOrWhiteSpace(heard))
        {
            if (GuardianPresenceState == GuardianPresenceState.Listening) GuardianPresenceState = GuardianPresenceState.Idle;
            return;
        }
        _lastHeardAt = DateTime.Now;
        // A conversation already running takes it as its next turn (RunConversationLoopAsync reads _bargeInUtterance).
        if (_conversationActive) return;
        _bargeInUtterance = null;
        _conversationActive = true;
        try { await RunConversationLoopAsync(listening, heard); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Conversation after barge-in failed: {ex.Message}"); }
        finally { EndConversation(); }
    }
}
