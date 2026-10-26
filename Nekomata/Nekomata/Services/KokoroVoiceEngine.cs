using System.IO;
using System.Reflection;

namespace Nekomata.UI.Services;

/// <summary>
/// Guardian's spoken voice, generated on this PC by Kokoro (KokoroSharp, MIT; Kokoro-82M model and voices, Apache 2.0).
/// One natural British voice for every line, and nothing sent to the cloud (AI-use policy). The model (~320 MB)
/// downloads once on first use; until it's loaded the other voices carry on. The voice can be changed with
/// Guardian:Voice in appsettings (British: bm_george, bm_lewis, bm_daniel, bm_fable; bf_emma, bf_isabella, bf_alice, bf_lily).
///
/// KokoroSharp is reached by type name rather than compiled against its namespaces, so a library update that moves
/// a namespace can't break Nekomata's build; anything unexpected simply leaves this engine unavailable.
/// </summary>
public sealed class KokoroVoiceEngine
{
    private readonly string _voiceName;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private object? _synthesizer;
    private object? _voice;
    private MethodInfo? _synthesize;
    private Task? _loading;

    public KokoroVoiceEngine(string? voiceName) => _voiceName = string.IsNullOrWhiteSpace(voiceName) ? "bm_george" : voiceName.Trim();

    public bool Ready => _synthesizer is not null && _voice is not null && _synthesize is not null;
    public string Status { get; private set; } = "not started";
    public string VoiceName => _voiceName;
    public string LastError { get; private set; } = "";

    /// <summary>Loads (downloading the first time) in the background. Safe to call repeatedly.</summary>
    public Task WarmUpAsync() => _loading ??= Task.Run(Load);

    /// <summary>True once loading has finished and failed; then Guardian falls back to other voices for good.</summary>
    public bool Failed => Status.StartsWith("unavailable", StringComparison.Ordinal);

    /// <summary>Waits (up to the timeout) for the model to finish loading, so the first lines after start-up
    /// are spoken by Kokoro rather than by a stand-in voice.</summary>
    public async Task<bool> WaitReadyAsync(TimeSpan timeout)
    {
        if (Ready || Failed) return Ready;
        await Task.WhenAny(WarmUpAsync(), Task.Delay(timeout));
        return Ready;
    }

    private void Load()
    {
        try
        {
            Status = "loading";
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "KokoroSharp")
                           ?? Assembly.Load("KokoroSharp");
            var synthType = Find(assembly, "KokoroWavSynthesizer");
            var managerType = Find(assembly, "KokoroVoiceManager");

            var load = synthType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "LoadModel" && m.GetParameters().All(p => p.IsOptional))
                .OrderBy(m => m.GetParameters().Length).First();
            _synthesizer = load.Invoke(null, load.GetParameters().Select(_ => Type.Missing).ToArray());

            var getVoice = managerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "GetVoice" && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(string));
            _voice = getVoice.Invoke(null, new object?[] { _voiceName }.Concat(getVoice.GetParameters().Skip(1).Select(_ => Type.Missing)).ToArray());

            _synthesize = synthType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Synthesize" && m.ReturnType == typeof(byte[]) && m.GetParameters().Length >= 2 &&
                            m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters().Skip(2).All(p => p.IsOptional))
                .OrderBy(m => m.GetParameters().Length).First();
            Status = $"ready ({_voiceName})";
        }
        catch (Exception ex)
        {
            _synthesizer = null;
            Status = "unavailable: " + (ex.InnerException ?? ex).GetBaseException().Message;
            CrashLog.Write("Kokoro voice", ex);
        }
    }

    /// <summary>WAV audio for one chunk of text, or null if the engine isn't ready or failed.</summary>
    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (!Ready) return null;
        text = Nekomata.Core.Guardian.KokoroText.Prepare(text);
        if (text.Length == 0) return null;
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var args = new object?[] { text, _voice }.Concat(_synthesize!.GetParameters().Skip(2).Select(_ => Type.Missing)).ToArray();
                return _synthesize.Invoke(_synthesizer, args) as byte[];
            });
        }
        catch (Exception ex)
        {
            LastError = (ex.InnerException ?? ex).GetBaseException().Message;
            CrashLog.Write("Kokoro synthesis", ex);
            return null;
        }
        finally { _gate.Release(); }
    }

    private static Type Find(Assembly assembly, string name) =>
        assembly.GetTypes().FirstOrDefault(t => t.Name == name) ?? throw new InvalidOperationException($"{name} not found in KokoroSharp");
}
