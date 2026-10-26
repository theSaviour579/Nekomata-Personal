using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Autonomy;
using Nekomata.Integrations.MicrosoftGraph.Meetings;

namespace Nekomata.UI.ViewModels;

// What Guardian may do on its own - one settings file, a section on the status page (alongside the integration
// checks, with what each feature has done today), and a spoken summary ("what can you do on your own?").
// Ask-mode actions are held as a single pending proposal that "do it" applies within 20 minutes.
public partial class MainViewModel
{
    private static readonly TimeSpan PendingAutonomyLifetime = TimeSpan.FromMinutes(20);
    private GuardianAutonomySettings _autonomy = new();
    private (string Description, Func<Task> Apply, DateTimeOffset Expires)? _pendingAutonomy;

    public ObservableCollection<AutonomySettingRow> AutonomyRows { get; } = [];
    public string AutonomySummary => $"{AutonomyRows.Count(r => r.SelectedOption?.Mode == AutonomyMode.Auto)} on their own · " +
                                     $"{AutonomyRows.Count(r => r.SelectedOption?.Mode == AutonomyMode.Ask)} ask first · {AutonomyRows.Count(r => r.IsOff)} off";

    private static string AutonomySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "autonomy.json");

    private void InitialiseAutonomy()
    {
        try
        {
            if (File.Exists(AutonomySettingsPath))
                _autonomy = JsonSerializer.Deserialize<GuardianAutonomySettings>(File.ReadAllText(AutonomySettingsPath)) ?? new();
            else if (File.Exists(AutopilotSettingsPath) &&
                     JsonDocument.Parse(File.ReadAllText(AutopilotSettingsPath)).RootElement.TryGetProperty("Enabled", out var enabled) &&
                     enabled.ValueKind == JsonValueKind.False)
            {
                // Carry over an earlier "autopilot off".
                _autonomy.Set(AutonomyAction.MoveOwnBlocks, AutonomyMode.Off);
                _autonomy.Set(AutonomyAction.RescheduleMissedBlocks, AutonomyMode.Off);
                SaveAutonomy();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autonomy settings unreadable: {ex.Message}"); }
        foreach (var info in GuardianAutonomySettings.Catalogue)
            AutonomyRows.Add(new AutonomySettingRow(info, _autonomy.Get(info.Action), SetAutonomy));
    }

    public AutonomyMode GetAutonomy(AutonomyAction action) => _autonomy.Get(action);

    public void SetAutonomy(AutonomyAction action, AutonomyMode mode)
    {
        _autonomy.Set(action, mode);
        SaveAutonomy();
        OnPropertyChanged(nameof(AutopilotEnabled));
        OnPropertyChanged(nameof(AutonomySummary));
        // Keep the status page in step when a setting changes by voice ("autopilot off").
        var row = AutonomyRows.FirstOrDefault(r => r.Action == action);
        var wanted = _autonomy.Get(action);
        if (row is not null && row.SelectedOption?.Mode != wanted) row.SelectedOption = row.Options.FirstOrDefault(o => o.Mode == wanted);
        _ = RefreshAutonomyStatusAsync();
        if (action is AutonomyAction.MoveOwnBlocks or AutonomyAction.RescheduleMissedBlocks) _autopilotLastReplan = DateTimeOffset.MinValue;
    }

    private void SaveAutonomy()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutonomySettingsPath)!);
            File.WriteAllText(AutonomySettingsPath, JsonSerializer.Serialize(_autonomy));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Autonomy settings not saved: {ex.Message}"); }
    }

    private void SetPendingAutonomyAction(string description, Func<Task> apply) =>
        _pendingAutonomy = (description, apply, DateTimeOffset.Now + PendingAutonomyLifetime);

    private async Task ConfirmPendingAutonomyAsync(string input)
    {
        if (_pendingAutonomy is not { } pending || pending.Expires < DateTimeOffset.Now)
        {
            // Not ours: "go ahead" may be meant for a proposal in the Guardian conversation.
            _pendingAutonomy = null;
            await FallBackToGuardianAsync(input);
            return;
        }
        _pendingAutonomy = null;
        // The evidence for "did confirmed actions work?" (the scorecard).
        try
        {
            await pending.Apply();
            _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
            {
                EventType = "ActionConfirmed", Category = "Guardian", Title = "Go-ahead given", Narrative = pending.Description,
                Outcome = "Done", Significance = 55
            });
        }
        catch (Exception ex)
        {
            _ = RecordWorkdayEventAsync(new Nekomata.Models.Guardian.WorkdayEvent
            {
                EventType = "ActionConfirmed", Category = "Guardian", Title = "Go-ahead given", Narrative = $"{pending.Description}: {ex.GetBaseException().Message}",
                Outcome = "Failed", Significance = 70
            });
            await RespondAndSpeakAsync($"That didn't work: {ex.GetBaseException().Message}");
        }
    }

    [RelayCommand]
    private async Task OpenAutonomySettingsAsync()
    {
        await ShowDiagnosticsAsync();
        await RefreshAutonomyStatusAsync();
    }

    /// <summary>What each feature has actually done today, and anything it is waiting on.</summary>
    private async Task RefreshAutonomyStatusAsync()
    {
        try
        {
            var today = DateTimeOffset.Now.Date;
            foreach (var row in AutonomyRows)
            {
                row.NeedsAttention = false;
                if (row.IsOff) { row.Status = "Off"; continue; }
                row.Status = row.Action switch
                {
                    AutonomyAction.MoveOwnBlocks => Count(_autopilotMovesPerBlock.Values.Sum(), "block move", "today"),
                    AutonomyAction.RescheduleMissedBlocks => Count(_autopilotMissedHandled.Count(id => !id.StartsWith("unplaced:", StringComparison.Ordinal)), "finished block checked", "today"),
                    AutonomyAction.PromisesToTasks => _promisesCache is { } p && p.Day == today
                        ? $"{p.Summary.PromiseCount} promise{(p.Summary.PromiseCount == 1 ? "" : "s")} found in today's sent mail · checked {p.At.LocalDateTime:HH:mm}"
                        : "Not checked yet today",
                    AutonomyAction.BookTimeForPromises => _autonomy.IsOn(AutonomyAction.PromisesToTasks) ? "Ready" : "Needs promises to tasks switched on",
                    AutonomyAction.OffPlanNudges => $"{Count(_autopilotNudged.Count, "nudge", "today")} · {_autopilotProfile.NudgeAfterMinutes} minutes into a block",
                    AutonomyAction.MorningPlanOffer => "After the morning briefing",
                    AutonomyAction.WeeklySlipping => "Mondays after the briefing",
                    AutonomyAction.PrepareReplies => Count(_preparedToday.Count(x => x.At.Date == today), "reply drafted", "today"),
                    AutonomyAction.SelfTuning => _autopilotProfile.LastRun is { } last ? $"Last reviewed {last:ddd d MMM}" : "First review on the next working morning",
                    AutonomyAction.DelegationChasers => Count(_delegations.Count(d => d.Status != "Done"), "handoff", "open"),
                    AutonomyAction.MeetingFollowThrough => "Checking permission…",
                    _ => ""
                };
                if (row.Action == AutonomyAction.BookTimeForPromises && !_autonomy.IsOn(AutonomyAction.PromisesToTasks)) row.NeedsAttention = true;
            }

            var meetings = AutonomyRows.FirstOrDefault(r => r.Action == AutonomyAction.MeetingFollowThrough);
            if (meetings is { IsOff: false })
            {
                var granted = await _services.GetRequiredService<ITeamsTranscriptService>().HasConsentAsync();
                meetings.Status = granted ? "Teams transcript permission granted" : "Needs the Teams transcript permission - say \"enable meeting follow-through\"";
                meetings.NeedsAttention = !granted;
            }
            OnPropertyChanged(nameof(AutonomySummary));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Autonomy status failed safely: {ex.Message}");
        }

        static string Count(int n, string noun, string when) => $"{(n == 0 ? "No" : n.ToString())} {noun}{(n == 1 ? "" : "s")} {when}";
    }

    /// <summary>Plain-text autonomy lines for the copied diagnostics report.</summary>
    public string FormatAutonomyReport() =>
        "GUARDIAN AUTONOMY" + Environment.NewLine + string.Join(Environment.NewLine,
            AutonomyRows.Select(r => $"- {r.Label}: {r.SelectedOption?.Label ?? "?"} · {r.Status}"));

    private async Task DescribeAutonomyAsync()
    {
        var message = _autonomy.Describe() + " You can change any of it on the status page.";
        ShowPaletteResponse(message);
        await OpenAutonomySettingsAsync();
        await SpeakGuardianAsync(message, true);
    }
}
