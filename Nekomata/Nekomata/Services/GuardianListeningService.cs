using Microsoft.Extensions.Configuration;
using NAudio.Wave;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Nekomata.UI.Services;

// Mirrors GuardianSpeechService's shape (same OpenAI key lookup, same plain-HttpClient
// style) but for the input side: records from the default microphone and transcribes
// with OpenAI, so Guardian can be talked to rather than only typed at or dictated to
// via the old fixed-length offline SAPI capture.
public sealed class GuardianListeningService : IDisposable
{
    private static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DefaultSilenceTimeout = TimeSpan.FromMilliseconds(1100);
    private const double SpeechThreshold = 400;

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly string? _openAiApiKey;
    private bool _disposed;

    public GuardianListeningService(IConfiguration configuration)
    {
        _openAiApiKey = (configuration["OpenAI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"))?.Trim();
    }

    // Transcription runs locally (LocalTranscriber), so listening no longer needs the OpenAI key.
    public bool NeuralListeningAvailable => true;

    // Records until the caller has been quiet for a beat (so a sentence isn't cut off
    // mid-pause), the hard cap elapses, or cancellationToken fires early (the user
    // pressing the same hotkey again to stop deliberately). Returns null when nothing
    // usable was captured or OpenAI is not configured; the caller decides whether to
    // fall back to something else.
    public Task<string?> ListenAndTranscribeAsync(CancellationToken cancellationToken = default) =>
        ListenAndTranscribeAsync(DefaultMaxDuration, DefaultSilenceTimeout, cancellationToken);

    // Conversation follow-ups: open the microphone, but give up quietly if the user hasn't
    // started speaking within startWindow, so silence ends the conversation naturally.
    public Task<string?> ListenForFollowUpAsync(TimeSpan startWindow, CancellationToken cancellationToken = default) =>
        ListenAndTranscribeAsync(DefaultMaxDuration, DefaultSilenceTimeout, cancellationToken, startWindow);

    public async Task<string?> ListenAndTranscribeAsync(TimeSpan maxDuration, TimeSpan silenceTimeout, CancellationToken cancellationToken,
        TimeSpan? noSpeechTimeout = null)
    {
        if (!NeuralListeningAvailable) return null;

        byte[] wav;
        try
        {
            wav = await RecordAsync(maxDuration, silenceTimeout, noSpeechTimeout, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Microphone capture failed: {ex.Message}");
            return null;
        }
        if (wav.Length == 0) return null;

        try
        {
            var transcript = await TranscribeLocallyAsync(wav, cancellationToken);
            return string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenAI transcription failed: {ex.Message}");
            return null;
        }
    }

    private static Task<byte[]> RecordAsync(TimeSpan maxDuration, TimeSpan silenceTimeout, TimeSpan? noSpeechTimeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new MemoryStream();
        var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
        var writer = new WaveFileWriter(buffer, waveIn.WaveFormat);
        var started = DateTime.UtcNow;
        var lastLoudAt = DateTime.UtcNow;
        var speechDetected = false;
        var stopped = false;
        var gate = new object();

        void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
            }
            try { waveIn.StopRecording(); } catch { /* already stopping */ }
        }

        // A short poll rather than separate timers for the cap and the silence check,
        // to keep the state machine in one place and avoid two timers racing to stop
        // the same recording.
        var watchdog = new System.Timers.Timer(200) { AutoReset = true };
        watchdog.Elapsed += (_, _) =>
        {
            var now = DateTime.UtcNow;
            if (now - started >= maxDuration) { Stop(); return; }
            if (!speechDetected && noSpeechTimeout is { } window && now - started >= window) { Stop(); return; }
            if (speechDetected && now - lastLoudAt >= silenceTimeout) Stop();
        };

        waveIn.DataAvailable += (_, args) =>
        {
            lock (gate)
            {
                if (stopped) return;
                writer.Write(args.Buffer, 0, args.BytesRecorded);
            }
            if (AverageAmplitude(args.Buffer, args.BytesRecorded) > SpeechThreshold)
            {
                speechDetected = true;
                lastLoudAt = DateTime.UtcNow;
            }
        };

        waveIn.RecordingStopped += (_, args) =>
        {
            watchdog.Stop();
            watchdog.Dispose();
            var result = Array.Empty<byte>();
            try
            {
                // Flush patches the RIFF/data chunk sizes into the buffer without
                // closing it, so the bytes can still be read back out below.
                writer.Flush();
                var captured = buffer.ToArray();
                if (speechDetected && captured.Length > 44) result = captured;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not finalise the recorded clip: {ex.Message}");
            }
            waveIn.Dispose();
            if (args.Exception != null)
                System.Diagnostics.Debug.WriteLine($"Microphone recording ended with an error: {args.Exception.Message}");
            completion.TrySetResult(result);
        };

        var registration = cancellationToken.CanBeCanceled ? cancellationToken.Register(Stop) : default;

        waveIn.StartRecording();
        watchdog.Start();

        return completion.Task.ContinueWith(task =>
        {
            registration.Dispose();
            return task.Result;
        }, TaskScheduler.Default);
    }

    // Barge-in: listens while Guardian is talking. The microphone also hears Guardian through the speakers, so the level
    // of that echo is learned from the first moments of playback and kept as a slowly moving baseline; the user counts as
    // talking when the level stays well above it (3x, and at least a clear speaking level) for 400 ms. Then onBargeIn
    // fires (the caller stops speaking), the last 1.5 s are kept so his first words aren't lost, recording carries on
    // until he pauses, and the clip is transcribed. cancellationToken only stops it before he has started talking.
    public Task<string?> ListenForBargeInAsync(Func<bool> isPlaying, Action onBargeIn, CancellationToken cancellationToken)
    {
        if (!NeuralListeningAvailable) return Task.FromResult<string?>(null);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        var preRoll = new Queue<byte[]>();
        var buffer = new MemoryStream();
        var writer = new WaveFileWriter(buffer, waveIn.WaveFormat);
        var gate = new object();
        var triggered = false;
        var stopped = false;
        double baseline = 0;
        var playingBuffers = 0;
        var loudRun = 0;
        var triggeredAt = DateTime.MinValue;
        var lastLoudAt = DateTime.MinValue;

        void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true;
            }
            try { waveIn.StopRecording(); } catch { /* already stopping */ }
        }

        waveIn.DataAvailable += (_, args) =>
        {
            var level = AverageAmplitude(args.Buffer, args.BytesRecorded);
            var chunk = args.Buffer.AsSpan(0, args.BytesRecorded).ToArray();
            lock (gate)
            {
                if (stopped) return;
                if (triggered)
                {
                    writer.Write(chunk, 0, chunk.Length);
                    if (level > SpeechThreshold) lastLoudAt = DateTime.UtcNow;
                    var now = DateTime.UtcNow;
                    if (now - lastLoudAt >= DefaultSilenceTimeout || now - triggeredAt >= TimeSpan.FromSeconds(30))
                        _ = Task.Run(Stop);
                    return;
                }

                preRoll.Enqueue(chunk);
                while (preRoll.Count > 15) preRoll.Dequeue();
                if (!isPlaying()) { loudRun = 0; return; }

                playingBuffers++;
                if (playingBuffers <= 6)
                {
                    baseline = baseline == 0 ? level : baseline * 0.7 + level * 0.3;
                    return;
                }
                var threshold = Math.Max(SpeechThreshold * 2.25, baseline * 3);
                if (level > threshold) loudRun++;
                else loudRun = 0;
                baseline = baseline * (level > threshold ? 0.98 : 0.95) + level * (level > threshold ? 0.02 : 0.05);
                if (loudRun < 4) return;

                triggered = true;
                triggeredAt = lastLoudAt = DateTime.UtcNow;
                foreach (var earlier in preRoll) writer.Write(earlier, 0, earlier.Length);
                preRoll.Clear();
            }
            try { onBargeIn(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Barge-in handler failed: {ex.Message}"); }
        };

        waveIn.RecordingStopped += (_, _) =>
        {
            var result = Array.Empty<byte>();
            try
            {
                writer.Flush();
                if (triggered && buffer.Length > 44) result = buffer.ToArray();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not finalise the barge-in clip: {ex.Message}"); }
            waveIn.Dispose();
            completion.TrySetResult(result);
        };

        var registration = cancellationToken.Register(() =>
        {
            bool shouldStop;
            lock (gate) shouldStop = !triggered;
            if (shouldStop) Stop();
        });

        try { waveIn.StartRecording(); }
        catch (Exception ex)
        {
            registration.Dispose();
            waveIn.Dispose();
            System.Diagnostics.Debug.WriteLine($"Barge-in microphone unavailable: {ex.Message}");
            return Task.FromResult<string?>(null);
        }

        return TranscribeWhenDoneAsync();

        async Task<string?> TranscribeWhenDoneAsync()
        {
            var wav = await completion.Task;
            registration.Dispose();
            if (wav.Length == 0) return null;
            try
            {
                var transcript = await TranscribeLocallyAsync(wav, CancellationToken.None);
                return string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Barge-in transcription failed: {ex.Message}");
                return null;
            }
        }
    }

    private static double AverageAmplitude(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2) return 0;
        long sum = 0;
        var samples = 0;
        for (var i = 0; i + 1 < bytesRecorded; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i);
            sum += Math.Abs((int)sample);
            samples++;
        }
        return samples == 0 ? 0 : (double)sum / samples;
    }

    // AI-use policy: audio of the user speaking names people, so it is transcribed on this PC, never in the cloud.
    private static Task<string?> TranscribeLocallyAsync(byte[] wav, CancellationToken cancellationToken) =>
        LocalTranscriber.TranscribeAsync(wav, cancellationToken);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
