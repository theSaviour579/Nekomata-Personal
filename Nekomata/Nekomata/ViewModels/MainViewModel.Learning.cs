using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Autonomy;
using Nekomata.Core.Guardian.Autopilot;
using Nekomata.Data.Repositories;

namespace Nekomata.UI.ViewModels;

// Learning from the user's corrections. Autopilot logs what it does (moves) and how he responds (undos, nudges acted on
// or let go) to a local file. Once a week the learner turns that, plus when his focus sessions actually happen, into a
// small profile: hours not to move blocks into, the window to protect for deep work, and how soon to nudge. Each
// change is said out loud once, with the reason. "What have you learned about me?" reads the profile back.
public partial class MainViewModel
{
    private static readonly TimeSpan SelfTuningInterval = TimeSpan.FromDays(7);
    private AutopilotProfile _autopilotProfile = new();
    private readonly Dictionary<string, (DateTime From, DateTime To)> _lastAutopilotMoves = new(StringComparer.Ordinal);
    private readonly HashSet<string> _nudgeFollowed = new(StringComparer.Ordinal);
    private bool _selfTuning;

    private static string AutopilotLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "autopilot-log.jsonl");
    private static string AutopilotProfilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "autopilot-profile.json");

    private TimeSpan NudgeAfter => TimeSpan.FromMinutes(Math.Clamp(_autopilotProfile.NudgeAfterMinutes, 5, 30));

    private void InitialiseLearning()
    {
        try
        {
            if (File.Exists(AutopilotProfilePath))
                _autopilotProfile = JsonSerializer.Deserialize<AutopilotProfile>(File.ReadAllText(AutopilotProfilePath)) ?? new();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autopilot profile unreadable: {ex.Message}"); }
    }

    private void LogAutopilot(AutopilotLogKind kind, string blockId, DateTime? from = null, DateTime? to = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutopilotLogPath)!);
            File.AppendAllText(AutopilotLogPath, JsonSerializer.Serialize(new AutopilotLogEntry(DateTime.Now, kind, blockId, from, to)) + Environment.NewLine);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autopilot log not written: {ex.Message}"); }
    }

    private static List<AutopilotLogEntry> ReadAutopilotLog(DateTime since)
    {
        var entries = new List<AutopilotLogEntry>();
        try
        {
            if (!File.Exists(AutopilotLogPath)) return entries;
            foreach (var line in File.ReadLines(AutopilotLogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<AutopilotLogEntry>(line);
                    if (entry is not null && entry.At >= since) entries.Add(entry);
                }
                catch { /* skip a damaged line */ }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autopilot log unreadable: {ex.Message}"); }
        return entries;
    }

    private async Task RunSelfTuningIfDueAsync()
    {
        if (_selfTuning || !_autonomy.IsOn(AutonomyAction.SelfTuning)) return;
        var now = DateTime.Now;
        if (_autopilotProfile.LastRun is { } last && now - last < SelfTuningInterval) return;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || now.Hour < 9) return;
        _selfTuning = true;
        try
        {
            Think("Looking back at the last four weeks to see what I should do differently");
            var sessions = (await SafeAsync(() => _services.GetRequiredService<IMissionSessionRepository>()
                    .GetBetweenAsync(now.AddDays(-28), now), []))
                .Where(s => s.ActualDurationMinutes > 0)
                .Select(s => new FocusSession(s.StartedAt, s.ActualDurationMinutes)).ToList();
            var learned = AutopilotLearning.Learn(_autopilotProfile, ReadAutopilotLog(now.AddDays(-28)), sessions, now);
            _autopilotProfile = learned;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AutopilotProfilePath)!);
                await File.WriteAllTextAsync(AutopilotProfilePath, JsonSerializer.Serialize(learned));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autopilot profile not saved: {ex.Message}"); }

            if (learned.Notes.Count == 0) { Think("Checked how the last few weeks went; nothing to change"); return; }
            Think(learned.Notes[0]);
            await SpeakGuardianAsync("I've been learning from the last few weeks. " + string.Join(" ", learned.Notes) +
                                     " Say undo learning if you'd rather I didn't.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Self-tuning failed safely: {ex.Message}");
        }
        finally
        {
            _selfTuning = false;
        }
    }

    private async Task ShowLearnedProfileAsync()
    {
        var p = _autopilotProfile;
        var parts = new List<string>();
        if (p.PreferredWindowLabel.Length > 0) parts.Add($"Your best focus time is {p.PreferredWindowLabel}, so I keep it for your deepest work.");
        if (p.AvoidHours.Count > 0) parts.Add($"I don't move focus blocks into {SpokenList(p.AvoidHours.Select(h => $"the {h:00}:00 hour"))}, because you kept undoing it.");
        parts.Add($"I mention drift {p.NudgeAfterMinutes} minutes into a block.");
        parts.Add(p.LastRun is { } last ? $"I last reviewed this on {last:dddd d MMMM}." : "I haven't done a weekly review yet; the first one runs on the next working morning.");
        var message = string.Join(" ", parts);
        ShowPaletteResponse(message);
        await SpeakGuardianAsync(message, true);
    }

    private async Task ResetLearningAsync()
    {
        _autopilotProfile = new AutopilotProfile { LastRun = DateTime.Now };
        try { await File.WriteAllTextAsync(AutopilotProfilePath, JsonSerializer.Serialize(_autopilotProfile)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autopilot profile not saved: {ex.Message}"); }
        const string message = "Done. I've gone back to my defaults and I'll look again in a week.";
        ShowPaletteResponse(message);
        await SpeakGuardianAsync(message, true);
    }
}
