using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.Services;
using System.Globalization;
using System.Speech.Recognition;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task DictateCommandPaletteAsync()
    {
        if (SpeechInputActive) return;
        SpeechInputActive = true;
        GuardianPresenceState = GuardianPresenceState.Listening;
        CommandPaletteStatus = "Listening… speak naturally.";
        try
        {
            var listening = _services.GetRequiredService<GuardianListeningService>();
            var heard = listening.NeuralListeningAvailable
                ? await listening.ListenAndTranscribeAsync()
                : await Task.Run(RecognizeOnce);
            if (!string.IsNullOrWhiteSpace(heard))
            {
                _lastHeardAt = DateTime.Now;
                CommandPaletteInput = heard;
                CommandPaletteStatus = $"Heard: {heard}";
            }
            else CommandPaletteStatus = "Nothing was recognised. Try again or type instead.";
        }
        catch (Exception ex)
        {
            CommandPaletteStatus = $"Speech input unavailable: {ex.Message}";
        }
        finally
        {
            SpeechInputActive = false;
            if (GuardianPresenceState == GuardianPresenceState.Listening)
                GuardianPresenceState = GuardianPresenceState.Idle;
        }
    }

    private static string? RecognizeOnce()
    {
        using var recognizer = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SetInputToDefaultAudioDevice();
        return recognizer.Recognize(TimeSpan.FromSeconds(12))?.Text;
    }
}
