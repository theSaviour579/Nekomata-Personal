using System.Globalization;
using System.IO;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using Nekomata.AI.Privacy;
using Whisper.net;

namespace Nekomata.UI.Services;

/// <summary>
/// Speech to text on this PC (AI-use policy: what the user says names people, so audio never goes to the cloud).
/// Uses Whisper running locally when a model file is in %LOCALAPPDATA%\Nekomata\models (ggml-base.en.bin is the
/// recommended one, ggml-small.en.bin is more accurate but slower); otherwise the built-in Windows dictation.
/// </summary>
public static partial class LocalTranscriber
{
    private static readonly object Gate = new();
    private static WhisperFactory? s_factory;
    private static string? s_factoryPath;

    public static string ModelFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "models");

    /// <summary>The Whisper model in use, or null when Windows dictation is used instead.</summary>
    public static string? ModelPath
    {
        get
        {
            try
            {
                if (!Directory.Exists(ModelFolder)) return null;
                foreach (var preferred in new[] { "ggml-base.en.bin", "ggml-small.en.bin", "ggml-base.bin", "ggml-small.bin" })
                    if (File.Exists(Path.Combine(ModelFolder, preferred))) return Path.Combine(ModelFolder, preferred);
                return Directory.EnumerateFiles(ModelFolder, "ggml-*.bin").FirstOrDefault();
            }
            catch { return null; }
        }
    }

    public static string EngineName => ModelPath is { } p ? $"Whisper on this PC ({Path.GetFileName(p)})" : "Windows dictation on this PC";

    public static async Task<string?> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        string? text;
        var model = ModelPath;
        try
        {
            text = model is not null
                ? await WhisperAsync(model, wav, cancellationToken)
                : await Task.Run(() => WindowsDictation(wav), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && model is not null)
        {
            System.Diagnostics.Debug.WriteLine($"Local Whisper failed, using Windows dictation: {ex.Message}");
            text = await Task.Run(() => WindowsDictation(wav), cancellationToken);
        }
        AiSendLog.Record(new(DateTimeOffset.Now, "transcription (local)", model is null ? "windows-dictation" : Path.GetFileName(model), 0,
            new Dictionary<string, int>(), Note: "audio stays on this PC"));
        text = NonSpeech().Replace(text ?? "", " ").Trim();
        return text.Length == 0 ? null : Regex.Replace(text, @"\s+", " ");
    }

    private static async Task<string> WhisperAsync(string model, byte[] wav, CancellationToken cancellationToken)
    {
        WhisperFactory factory;
        lock (Gate)
        {
            if (s_factory is null || s_factoryPath != model)
            {
                s_factory?.Dispose();
                s_factory = WhisperFactory.FromPath(model);
                s_factoryPath = model;
            }
            factory = s_factory;
        }
        using var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .WithPrompt("Guardian, Nekomata, Teams, Outlook, Spotify, do it.")
            .Build();
        using var stream = new MemoryStream(wav);
        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            sb.Append(segment.Text);
        return sb.ToString();
    }

    private static string WindowsDictation(byte[] wav)
    {
        SpeechRecognitionEngine engine;
        try { engine = new SpeechRecognitionEngine(new CultureInfo("en-GB")); }
        catch { engine = new SpeechRecognitionEngine(); }
        using (engine)
        {
            engine.LoadGrammar(new DictationGrammar());
            using var stream = new MemoryStream(wav);
            engine.SetInputToWaveStream(stream);
            var sb = new StringBuilder();
            RecognitionResult? result;
            while ((result = engine.Recognize(TimeSpan.FromSeconds(30))) is not null)
                sb.Append(result.Text).Append(' ');
            return sb.ToString();
        }
    }

    // Whisper marks silence and noise like "[BLANK_AUDIO]" or "(wind blowing)".
    [GeneratedRegex(@"\[[^\]]*\]|\([^\)]*\)|\*[^*]*\*")]
    private static partial Regex NonSpeech();
}
