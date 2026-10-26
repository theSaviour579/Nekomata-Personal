using System.IO;
using System.Text;
using Nekomata.Core.Guardian;
using Nekomata.Core.Guardian.Commands;

namespace Nekomata.UI.ViewModels;

// Protocols: named routines ("end of day protocol", "monday protocol"). Each step is an ordinary command run through
// the palette, one after another, waiting for Guardian to finish speaking in between; the results are gathered into one
// report on screen. A protocol stops if a call starts or the user says "stop". Built-in protocols live in Core
// (Protocols); the user's own are kept in protocols.json. Nothing here can confirm an action.
public partial class MainViewModel
{
    private static string ProtocolsPath => Path.Combine(DataFolder, "protocols.json");
    private List<Protocol>? _customProtocols;
    private List<Protocol> CustomProtocols => _customProtocols ??= LoadJson<List<Protocol>>(ProtocolsPath) ?? [];
    private bool _protocolRunning;
    private bool _protocolStop;

    private async Task RunProtocolAsync(string name, string detail)
    {
        // "stop protocol", "cancel the protocol", "abort protocol".
        var key = Protocols.Normalise(name);
        if (key is "stop" or "cancel" or "abort" or "end" or "halt")
        {
            if (!_protocolRunning) { await RespondAndSpeakAsync("No protocol is running."); return; }
            _protocolStop = true;
            _guardianSpeech.StopSpeaking();
            ShowPaletteResponse("Protocol stopped.");
            return;
        }
        var protocol = Protocols.Find(name, CustomProtocols);
        if (protocol is null)
        {
            await RespondAndSpeakAsync($"I don't have a protocol called {name}. I have: {string.Join(", ", AllProtocols().Select(p => p.Name))}.");
            return;
        }
        var steps = Protocols.StepsFor(protocol, detail);
        if (steps is null)
        {
            await RespondAndSpeakAsync($"The {protocol.Name} protocol needs a detail. Say, for example, {protocol.Name} protocol: and what it's about.");
            return;
        }
        if (_protocolRunning) { await RespondAndSpeakAsync("A protocol is already running."); return; }

        _protocolRunning = true;
        _protocolStop = false;
        var report = new StringBuilder($"{protocol.Name.ToUpperInvariant()} PROTOCOL · {DateTime.Now:ddd d MMM HH:mm}\n");
        var ran = 0;
        try
        {
            await SpeakGuardianAsync($"Running the {protocol.Name} protocol.", true);
            foreach (var step in steps)
            {
                if (_protocolStop || LiveCallReason() is not null) { report.AppendLine().AppendLine("Stopped before: " + step); break; }
                var match = GuardianCommandParser.Parse(step);
                if (match.Intent is GuardianCommandIntent.ConfirmPending or GuardianCommandIntent.RunProtocol or GuardianCommandIntent.Unknown) continue;
                var before = GuardianResponse;
                CommandPaletteInput = step;
                await ExecuteCommandPaletteAsync();
                await WaitForQuietAsync(TimeSpan.FromMinutes(3));
                ran++;
                var shown = GuardianResponse;
                if (!string.IsNullOrWhiteSpace(shown) && shown != before)
                    report.AppendLine().AppendLine($"── {step.ToUpperInvariant()} ──").AppendLine(shown.Trim());
            }
        }
        finally
        {
            _protocolRunning = false;
            GuardianResponse = report.ToString().TrimEnd();
            GuardianPanelExpanded = true;
            await RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
            {
                EventType = "ProtocolRun", Category = "Guardian", Title = $"{protocol.Name} protocol",
                Narrative = $"{ran} of {steps.Count} steps run" + (detail.Length > 0 ? $" ({detail})" : ""), Outcome = _protocolStop ? "Stopped" : "Done", Significance = 20
            });
        }
        if (!_protocolStop) await SpeakGuardianAsync($"That's the {protocol.Name} protocol complete. Everything is in the Guardian panel.");
    }

    private async Task WaitForQuietAsync(TimeSpan limit)
    {
        var until = DateTimeOffset.Now + limit;
        await Task.Delay(400);
        while ((_guardianSpeakingDepth > 0 || GuardianBusy) && DateTimeOffset.Now < until && !_protocolStop)
            await Task.Delay(300);
    }

    private IEnumerable<Protocol> AllProtocols() =>
        CustomProtocols.Concat(Protocols.BuiltIn.Where(b => !CustomProtocols.Any(c => Protocols.Normalise(c.Name) == Protocols.Normalise(b.Name))));

    private void ListProtocols()
    {
        var sb = new StringBuilder("PROTOCOLS\n");
        foreach (var p in AllProtocols())
            sb.AppendLine().AppendLine($"{p.Name.ToUpperInvariant()}{(p.BuiltIn ? "" : " (yours)")} · {p.Description}")
              .AppendLine("   " + string.Join(" → ", p.Steps));
        sb.AppendLine().AppendLine("Say \"<name> protocol\" to run one. Make your own: create protocol lunch: plan my day; anything I need to worry about");
        ShowPaletteResponse(sb.ToString().TrimEnd());
        _ = SpeakGuardianAsync($"You have {AllProtocols().Count()} protocols: {SpokenList(AllProtocols().Select(p => p.Name).ToList())}.");
    }

    private async Task CreateProtocolAsync(string name, string stepsText)
    {
        name = name.Trim().TrimEnd('.');
        var steps = Protocols.ParseSteps(stepsText);
        var bad = steps.Where(s => GuardianCommandParser.Parse(s).Intent is GuardianCommandIntent.Unknown or GuardianCommandIntent.ConfirmPending
            or GuardianCommandIntent.RunProtocol or GuardianCommandIntent.CreateProtocol or GuardianCommandIntent.DeleteProtocol).ToList();
        if (name.Length == 0 || steps.Count == 0 || bad.Count > 0)
        {
            await RespondAndSpeakAsync(bad.Count > 0
                ? $"I can't use {(bad.Count == 1 ? "this step" : "these steps")}: {string.Join("; ", bad)}. Each step must be a command I already know, and a protocol can't say do it."
                : "Say it like this: create protocol lunch: plan my day; anything I need to worry about.");
            return;
        }
        CustomProtocols.RemoveAll(p => Protocols.Normalise(p.Name) == Protocols.Normalise(name));
        CustomProtocols.Add(new Protocol(name, $"Your protocol: {string.Join(", ", steps)}", steps, [], BuiltIn: false));
        SaveJson(ProtocolsPath, CustomProtocols);
        await RespondAndSpeakAsync($"The {name} protocol is set: {steps.Count} step{(steps.Count == 1 ? "" : "s")}. Say {name} protocol to run it.");
    }

    private async Task DeleteProtocolAsync(string name)
    {
        var removed = CustomProtocols.RemoveAll(p => Protocols.Normalise(p.Name) == Protocols.Normalise(name));
        if (removed > 0) SaveJson(ProtocolsPath, CustomProtocols);
        await RespondAndSpeakAsync(removed > 0 ? $"The {name} protocol is gone."
            : Protocols.Find(name, []) is not null ? "That one's built in, so it stays. You can make your own with the same name to replace it." : $"I don't have a {name} protocol.");
    }
}
