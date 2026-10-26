using Microsoft.Extensions.Configuration;
using NAudio.Wave;
using Nekomata.Core.Guardian;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Speech.Synthesis;
using Nekomata.AI.Privacy;

namespace Nekomata.UI.Services;

public sealed class GuardianSpeechService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private readonly string? _openAiApiKey;
    private readonly KokoroVoiceEngine _kokoro;
    private string? _lastSpokenText;
    private WaveOutEvent? _currentOutput;
    private DateTime _lastSpokenAt;
    private int _generation;
    private bool _disposed;

    public GuardianSpeechService(IConfiguration configuration)
    {
        _openAiApiKey = (configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))?.Trim();
        // Guardian's main voice: Kokoro on this PC (downloads its model once, then offline).
        _kokoro = new KokoroVoiceEngine(configuration["Guardian:Voice"]);
        _ = _kokoro.WarmUpAsync();

        try
        {
            _synthesizer.SetOutputToDefaultAudioDevice();
            _synthesizer.Rate = 0;
            _synthesizer.Volume = 90;
            LocalVoiceAvailable = _synthesizer.GetInstalledVoices().Any(voice => voice.Enabled);
            // A British Windows voice where one is installed (Hazel, George, Susan...).
            var british = _synthesizer.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name == "en-GB");
            if (british is not null) _synthesizer.SelectVoice(british.VoiceInfo.Name);
        }
        catch (InvalidOperationException)
        {
            LocalVoiceAvailable = false;
        }
    }

    public bool LocalVoiceAvailable { get; }
    public bool NeuralVoiceAvailable => !string.IsNullOrWhiteSpace(_openAiApiKey);
    public bool IsAvailable => NeuralVoiceAvailable || LocalVoiceAvailable || _kokoro.Ready;
    /// <summary>The local Kokoro voice's state, for "which voice are you using?" and the AI report.</summary>
    public string KokoroStatus => _kokoro.Status;
    public bool Enabled { get; set; } = true;

    // True while neural audio is actually coming out of the speakers (not while a chunk is being fetched).
    public bool IsPlaying => _currentOutput?.PlaybackState == PlaybackState.Playing;

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
            StopSpeaking();

        await _speechGate.WaitAsync();
        try
        {
            if (!Enabled || _disposed)
                return;

            _lastSpokenText = spokenText;
            _lastSpokenAt = DateTime.UtcNow;

            var generation = Volatile.Read(ref _generation);
            // The local Kokoro voice speaks everything: one consistent voice, nothing sent anywhere. Just after start-up the
            // model is still loading (about 325 MB), so wait for it rather than letting another voice stand in.
            if (!_kokoro.Failed && await _kokoro.WaitReadyAsync(TimeSpan.FromSeconds(60)) && generation == Volatile.Read(ref _generation))
            {
                var local = await SpeakWithNeuralVoiceAsync(spokenText, generation, async chunk =>
                {
                    var wav = await _kokoro.SynthesizeAsync(chunk);
                    if (wav is null) LastVoiceProblem = $"{DateTime.Now:HH:mm} Kokoro produced no audio: {_kokoro.LastError}";
                    return (wav, wav is null);
                });
                if (local != NeuralOutcome.HardFailure) return;
            }
            // The OpenAI voice is only a stand-in if Kokoro can't run at all on this PC; if Kokoro fails on one line, the
            // Windows voice says it instead. AI-use policy: the cloud voice hears the exact words, so a line that names
            // anyone, or carries an address, a customer or an amount, never goes to it.
            if (_kokoro.Failed && NeuralVoiceAvailable && !Pseudonymiser.ContainsRestricted(spokenText))
            {
                var outcome = await SpeakWithNeuralVoiceAsync(spokenText, generation, FetchNeuralAudioAsync);
                // Only a hard failure (bad key, no audio device) drops to the Windows voice. A slow or flaky
                // request skips that sentence instead, because switching voice mid-answer sounds broken and the
                // words are on screen anyway.
                if (outcome != NeuralOutcome.HardFailure)
                    return;
            }

            if (LocalVoiceAvailable && generation == Volatile.Read(ref _generation))
                await SpeakWithWindowsVoiceAsync(spokenText);
        }
        catch (OperationCanceledException)
        {
            // Interrupted on purpose (StopSpeaking: a call ringing, barge-in, or a more urgent line). The local
            // SAPI voice reports that by throwing from its synchronous Speak; it is not an error.
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

    // The Windows voice, asynchronously: StopSpeaking cancels it and the cancellation arrives as a completed event, not
    // as an exception (the synchronous Speak threw OperationCanceledException, which the debugger kept breaking on).
    private Task SpeakWithWindowsVoiceAsync(string text)
    {
        // Only one line speaks at a time (the speech gate), so any completion is this one's.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, SpeakCompletedEventArgs e)
        {
            _synthesizer.SpeakCompleted -= Completed;
            done.TrySetResult();
        }
        _synthesizer.SpeakCompleted += Completed;
        var prompt = _synthesizer.SpeakAsync(text);
        if (prompt.IsCompleted) { _synthesizer.SpeakCompleted -= Completed; done.TrySetResult(); }
        return done.Task;
    }

    private enum NeuralOutcome { Spoke, SkippedOrStopped, HardFailure }

    // Long answers are split into sentence chunks. The first chunk is short so Guardian starts talking quickly, and
    // the next chunk is fetched while the current one plays. One long request used to run past the timeout, which
    // is why longer answers (like "what's slipping") dropped to the Windows voice while the briefing didn't.
    private const int FirstChunkChars = 220;
    private const int ChunkChars = 380;

    private async Task<NeuralOutcome> SpeakWithNeuralVoiceAsync(string text, int generation, Func<string, Task<(byte[]? Audio, bool Hard)>> fetch)
    {
        var chunks = SplitForSpeech(text);
        if (chunks.Count == 0) return NeuralOutcome.SkippedOrStopped;

        var spokeAny = false;
        var next = fetch(chunks[0]);
        for (var i = 0; i < chunks.Count; i++)
        {
            var (audio, hard) = await next;
            if (generation != Volatile.Read(ref _generation)) return NeuralOutcome.SkippedOrStopped;
            next = i + 1 < chunks.Count ? fetch(chunks[i + 1]) : null!;

            if (audio is null)
            {
                if (hard && !spokeAny) return NeuralOutcome.HardFailure;
                continue;
            }

            try
            {
                await PlayWaveAsync(audio);
                spokeAny = true;
            }
            catch (Exception ex)
            {
                // The audio arrived but couldn't be played (device problem): let the local voice try.
                System.Diagnostics.Debug.WriteLine($"Neural Guardian voice playback failed: {ex.Message}");
                LastVoiceProblem = $"{DateTime.Now:HH:mm} playback failed ({audio.Length:N0} bytes, starts {Convert.ToHexString(audio.AsSpan(0, Math.Min(8, audio.Length)))}): {ex.GetBaseException().Message}";
                CrashLog.Write("Voice playback", ex);
                return spokeAny ? NeuralOutcome.SkippedOrStopped : NeuralOutcome.HardFailure;
            }

            if (generation != Volatile.Read(ref _generation)) return NeuralOutcome.SkippedOrStopped;
        }

        return spokeAny ? NeuralOutcome.Spoke : NeuralOutcome.SkippedOrStopped;
    }

    // The request timeout grows with the text, since generation time does too. Transient failures (timeout,
    // network blip, 429/5xx) get two retries; a hard failure (401/403/400) does not.
    private async Task<(byte[]? Audio, bool Hard)> FetchNeuralAudioAsync(string text)
    {
        var timeout = TimeSpan.FromSeconds(12 + text.Length / 25.0);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var (audio, transient) = await FetchNeuralAudioOnceAsync(text, timeout);
            if (audio is not null) return (audio, false);
            if (!transient) return (null, true);
            if (attempt < 3) await Task.Delay(400 * attempt);
        }
        return (null, false);
    }

    private async Task<(byte[]? Audio, bool Transient)> FetchNeuralAudioOnceAsync(string text, TimeSpan timeoutAfter)
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

            AiSendLog.Record(new(DateTimeOffset.Now, "speech", "gpt-4o-mini-tts", text.Length, new Dictionary<string, int>()));
            using var timeout = new CancellationTokenSource(timeoutAfter);
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                System.Diagnostics.Debug.WriteLine($"Neural Guardian voice returned HTTP {status}.");
                return (null, status == 408 || status == 429 || status >= 500);
            }

            var audio = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return audio.Length == 0 ? (null, true) : (audio, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Neural Guardian voice request failed: {ex.Message}");
            return (null, true);
        }
    }

    internal static List<string> SplitForSpeech(string text)
    {
        var sentences = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var boundary = c == '\n' || ((c == '.' || c == '!' || c == '?' || c == ';') && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1])));
            if (!boundary) continue;
            var piece = text[start..(i + 1)].Trim();
            if (piece.Length > 0) sentences.Add(piece);
            start = i + 1;
        }
        var tail = text[start..].Trim();
        if (tail.Length > 0) sentences.Add(tail);

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var sentence in sentences.SelectMany(s => SplitLong(s, ChunkChars)))
        {
            var limit = chunks.Count == 0 ? FirstChunkChars : ChunkChars;
            if (current.Length > 0 && current.Length + 1 + sentence.Length > limit)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks;
    }

    private static IEnumerable<string> SplitLong(string sentence, int max)
    {
        while (sentence.Length > max)
        {
            var cut = sentence.LastIndexOf(", ", max, StringComparison.Ordinal);
            if (cut < max / 3) cut = sentence.LastIndexOf(' ', max);
            if (cut < max / 3) cut = max;
            yield return sentence[..(cut + 1)].Trim();
            sentence = sentence[(cut + 1)..].Trim();
        }
        if (sentence.Length > 0) yield return sentence;
    }

    // Cuts off whatever Guardian is saying right now, neural or local voice. Used for
    // interrupt-priority lines and when the user presses the talk hotkey mid-sentence.
    public void StopSpeaking()
    {
        Interlocked.Increment(ref _generation);
        try { _synthesizer.SpeakAsyncCancelAll(); } catch (InvalidOperationException) { }
        try { _currentOutput?.Stop(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not stop neural playback: {ex.Message}"); }
    }

    private static bool IsWav(byte[] a) => a.Length > 12 && a[0] == 'R' && a[1] == 'I' && a[2] == 'F' && a[3] == 'F';
    private static bool IsMp3(byte[] a) => a.Length > 3 && ((a[0] == 'I' && a[1] == 'D' && a[2] == '3') || (a[0] == 0xFF && (a[1] & 0xE0) == 0xE0));

    /// <summary>Why the last line didn't come out in the main voice, for "what have you sent to the AI?".</summary>
    public string LastVoiceProblem { get; private set; } = "";

    private async Task PlayWaveAsync(byte[] audio)
    {
        using var stream = new MemoryStream(audio, writable: false);
        // Kokoro gives WAV, the OpenAI voice MP3.
        using WaveStream reader = IsWav(audio) ? new WaveFileReader(stream)
            : IsMp3(audio) ? new Mp3FileReader(stream)
            // Bare samples (no file header): Kokoro's raw output, 24 kHz mono 16-bit.
            : new RawSourceWaveStream(stream, new WaveFormat(24000, 16, 1));
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
        _currentOutput = output;
        try
        {
            output.Play();
            await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(_currentOutput, output)) _currentOutput = null;
        }
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