using System.IO;
using System.Text.Json;

namespace Nekomata.UI.ViewModels;

// Guardian learns what is worth interrupting the user for. "That wasn't important" after an interruption lowers that
// kind of item (source + action, e.g. "Email reply" reminders) by one step; "always tell me about those" raises it.
// Two steps either way at most. The adjustment feeds GuardianInterruptionPolicy, which never lets it hide critical
// items or incident language, so a dislike can only ever move routine items to a natural break or the briefing.
// Stored per machine in %LOCALAPPDATA%\Nekomata\interruption-preferences.json; "reset my interruption
// preferences" clears it.
public partial class MainViewModel
{
    private const int InterruptionPreferenceStep = 20;
    private const int InterruptionPreferenceLimit = 40;
    private static readonly TimeSpan InterruptionFeedbackWindow = TimeSpan.FromMinutes(30);

    private sealed class InterruptionPreference
    {
        public string Label { get; set; } = "";
        public int Adjustment { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private Dictionary<string, InterruptionPreference>? _interruptionPreferences;
    private (string Key, DateTimeOffset At)? _lastInterruption;

    private static string InterruptionPreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "interruption-preferences.json");

    private static string InterruptionKindKey(string source, string actionKind) =>
        $"{source.Trim().ToLowerInvariant()}|{actionKind.Trim().ToLowerInvariant()}";

    private Dictionary<string, InterruptionPreference> InterruptionPreferences
    {
        get
        {
            if (_interruptionPreferences is not null) return _interruptionPreferences;
            try
            {
                _interruptionPreferences = File.Exists(InterruptionPreferencesPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, InterruptionPreference>>(File.ReadAllText(InterruptionPreferencesPath)) ?? []
                    : [];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Interruption preferences unreadable; starting fresh: {ex.Message}");
                _interruptionPreferences = [];
            }
            return _interruptionPreferences;
        }
    }

    private int InterruptionPreferenceFor(string source, string actionKind) =>
        InterruptionPreferences.TryGetValue(InterruptionKindKey(source, actionKind), out var preference) ? preference.Adjustment : 0;

    private void RememberInterruption(GuardianAttentionItem item) => _lastInterruption = (item.Key, DateTimeOffset.Now);

    // "That" = what Guardian most recently put in front of the user: the item he last asked about, or failing that
    // the most recent interruption.
    private GuardianAttentionItem? InterruptionFeedbackTarget()
    {
        var now = DateTimeOffset.Now;
        if (_lastSurfacedEntity is { EntityType: "AttentionItem" } surfaced && now - surfaced.SurfacedAt <= LastSurfacedEntityTimeout)
        {
            var item = AttentionItems.FirstOrDefault(x => x.Key.Equals(surfaced.Key, StringComparison.OrdinalIgnoreCase));
            if (item is not null) return item;
        }
        if (_lastInterruption is { } last && now - last.At <= InterruptionFeedbackWindow)
            return AttentionItems.FirstOrDefault(x => x.Key.Equals(last.Key, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    private void ApplyInterruptionFeedback(string argument)
    {
        if (argument == "reset")
        {
            InterruptionPreferences.Clear();
            SaveInterruptionPreferences();
            ShowPaletteResponse("Done. I've forgotten what I'd learned about which interruptions matter to you.");
            return;
        }

        if (ApplyBreakThroughFeedback(argument)) return;
        var target = InterruptionFeedbackTarget();
        if (target is null)
        {
            ShowPaletteResponse("I'm not sure which alert you mean. Say it straight after one comes up, or open it from Attention first.");
            return;
        }

        var key = InterruptionKindKey(target.Source, target.ActionKind);
        var label = $"{target.Source} items";
        _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
        {
            EventType = "InterruptionFeedback", Category = target.Source, Title = "Interruption feedback", Narrative = target.Title,
            Outcome = argument, Significance = 35
        });
        var preference = InterruptionPreferences.TryGetValue(key, out var existing) ? existing : new InterruptionPreference();
        var step = argument == "more" ? InterruptionPreferenceStep : -InterruptionPreferenceStep;
        var before = preference.Adjustment;
        preference.Adjustment = Math.Clamp(before + step, -InterruptionPreferenceLimit, InterruptionPreferenceLimit);
        preference.Label = label;
        preference.UpdatedAt = DateTimeOffset.Now;
        if (preference.Adjustment == 0) InterruptionPreferences.Remove(key);
        else InterruptionPreferences[key] = preference;
        SaveInterruptionPreferences();

        var atLimit = preference.Adjustment == before;
        if (argument == "more")
            ShowPaletteResponse(atLimit
                ? $"{label} are already as high as they go. I'll keep putting them in front of you."
                : $"Noted. I'll give {label} more priority from now on.");
        else
        {
            ResolveAttention(target.Key);
            ShowPaletteResponse((atLimit
                ? $"{label} are already as low as they go, so they'll wait for a natural break or your briefing."
                : $"Got it. I'll give {label} less priority from now on, and I've cleared that one.") +
                " Anything critical will still get through.");
        }
    }

    private void SaveInterruptionPreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InterruptionPreferencesPath)!);
            File.WriteAllText(InterruptionPreferencesPath, JsonSerializer.Serialize(InterruptionPreferences));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Interruption preferences not saved: {ex.Message}"); }
    }
}
