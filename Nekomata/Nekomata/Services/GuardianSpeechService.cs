using Microsoft.Extensions.Configuration;
using NAudio.Wave;
using Nekomata.Core.Guardian;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Speech.Synthesis;

namespace Nekomata.UI.Services;

public sealed class GuardianSpeechService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly string? _openAiApiKey;
    private string? _lastSpokenText;
    private DateTime _lastSpokenAt;
    private bool _disposed;

    public GuardianSpeechService(IConfiguration configuration)
    {
        _openAiApiKey = configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        try
        {
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.Rate = 0;
            _synthesizer.Volume = 90;
            LocalVoiceAvailable = _synthesizer.GetInstalledVoices().Any(voice => voice.Enabled);
        }
        catch (InvalidOperationException)
        {
            LocalVoiceAvailable = false;
        }
    }

    public bool LocalVoiceAvailable { get; }
    public bool NeuralVoiceAvailable => !string.IsNullOrWhiteSpace(_openAiApiKey);
    public bool IsAvailable => NeuralVoiceAvailable || LocalVoiceAvailable;
    public bool Enabled { get; set; } = true;

    public async Task SpeakAsync(string? text, bool interrupt = false)
    {
        if (!IsAvailable || !Enabled || _disposed || string.IsNullOrWhiteSpace(text))
            return;

        var spokenText = GuardianSpeechTextNormalizer.Normalize(text);
        if (spokenText.Length == 0)
            return;

        if (string.Equals(spokenText, _lastSpokenText, StringComparison.Ordinal) &&
            DateTime.UtcNow - _lastSpokenAt < TimeSpan.FromMinutes(2))
        {
            return;
        }

        if (interrupt)
            _synthesizer.SpeakAsyncCancelAll();

        await _speechGate.WaitAsync();
        try
        {
            if (!Enabled || _disposed)
                return;

            _lastSpokenText = spokenText;
            _lastSpokenAt = DateTime.UtcNow;

            if (NeuralVoiceAvailable && await TrySpeakWithNeuralVoiceAsync(spokenText))
                return;

            if (LocalVoiceAvailable)
                await Task.Run(() => _synthesizer.Speak(spokenText));
        }
        catch (InvalidOperationException)
        {
            // Audio output is unavailable. Keep Guardian usable without speech.
        }
        finally
        {
            _speechGate.Release();
        }
    }

    private async Task<bool> TrySpeakWithNeuralVoiceAsync(string text)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/audio/speech");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _openAiApiKey);
            request.Content = JsonContent.Create(new
            {
                model = "gpt-4o-mini-tts",
                voice = "marin",
                input = text,
                instructions = "You are Nekomata, a perceptive British operational intelligence speaking directly to one trusted person. Do not read the text like a script: understand it first, then deliver it as a considered observation you have just formed. Sound present, quietly intelligent and human. Vary cadence naturally, use short reflective beats before conclusions, soften transitions, and let occasional understated warmth or dry confidence emerge. Keep greetings effortless and brief. Move briskly through routine figures, slow slightly for decisions or risks, and reserve firm emphasis for genuinely urgent information. Avoid uniform sentence rhythm, list-reading, theatricality, breathiness, forced cheerfulness and announcer delivery.",
                response_format = "mp3",
                speed = 1.06
            });

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Neural Guardian voice returned HTTP {(int)response.StatusCode}; using local fallback.");
                return false;
            }

            var audio = await response.Content.ReadAsByteArrayAsync();
            if (audio.Length == 0)
                return false;

            await PlayWaveAsync(audio);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Neural Guardian voice unavailable; using local fallback: {ex.Message}");
            return false;
        }
    }

    private static async Task PlayWaveAsync(byte[] audio)
    {
        using var stream = new MemoryStream(audio, writable: false);
        using var reader = new Mp3FileReader(stream);
        using var output = new WaveOutEvent();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        output.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is not null)
                completion.TrySetException(args.Exception);
            else
                completion.TrySetResult();
        };

        output.Init(reader);
        output.Play();
        await completion.Task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _synthesizer.SpeakAsyncCancelAll();
        _synthesizer.Dispose();
        _httpClient.Dispose();
        _speechGate.Dispose();
    }
}