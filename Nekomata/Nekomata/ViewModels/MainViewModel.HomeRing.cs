using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.AI.Models.Actions;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;

/// <summary>Something chosen on the ring: an item and what to do with it ("start", "push", "brief", "open").</summary>
public sealed record HomeRingAction(HomeTimelineItem Item, string Action);

/// <summary>A focus block dragged along the ring to a new start time.</summary>
public sealed record HomeRingMove(HomeTimelineItem Item, DateTime NewStart);

// The ring is clickable: a focus block can be started now or pushed later, a meeting briefed, anything opened in
// Outlook. Guardian's own focus blocks can also be dragged round the ring to a new time; meetings never move.
public partial class MainViewModel
{
    [ObservableProperty] private double missionCoreProgress;
    [ObservableProperty] private int homeHeldCount;

    partial void OnMissionSessionProgressChanged(double value) => MissionCoreProgress = Math.Clamp(value / 100, 0, 1);

    private CalendarEvent? FindRingEvent(HomeTimelineItem item) =>
        _autopilotToday.Concat(_homeTomorrow).FirstOrDefault(e => !string.IsNullOrWhiteSpace(item.Id) && e.Id == item.Id)
        ?? _autopilotToday.Concat(_homeTomorrow).FirstOrDefault(e => e.Start.LocalDateTime == item.Start && CleanCalendarObjectiveTitle(e.Subject) == item.Title);

    [RelayCommand]
    private async Task HomeRingAsync(HomeRingAction? action)
    {
        if (action is null) return;
        var ev = FindRingEvent(action.Item);
        var now = DateTimeOffset.Now;
        switch (action.Action)
        {
            case "start" when ev is not null:
                if (MissionActive)
                {
                    ShowPaletteResponse($"You're still on {Workspace.CurrentMission?.Title ?? "another mission"}. Finish it or hold it for later first.");
                    return;
                }
                SetActiveFocusObjective(ev, now);
                OnPropertyChanged(nameof(Workspace));
                await BeginMissionAsync();
                break;
            case "push" when ev is not null:
                var moved = await ReplanAsync(now, new HashSet<string>(), new HashSet<string> { ev.Id }, announceIfNothing: null);
                if (moved == 0) ShowPaletteResponse($"I couldn't find anywhere later today for {action.Item.Title}.");
                break;
            case "brief":
                await RunHomeCommandAsync($"brief me on {action.Item.Title}");
                break;
            case "open" when ev is not null && !string.IsNullOrWhiteSpace(ev.WebLink):
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ev.WebLink) { UseShellExecute = true });
                break;
            default:
                ShowPaletteResponse("I couldn't find that calendar item any more. It may have just moved.");
                break;
        }
        await RefreshHomeAsync();
    }

    [RelayCommand]
    private async Task HomeRingMoveAsync(HomeRingMove? move)
    {
        if (move is null) return;
        var ev = FindRingEvent(move.Item);
        if (ev is null || !ev.IsNekomataManaged) { ShowPaletteResponse("Only Guardian's own focus blocks can be moved on the ring."); return; }
        var length = ev.End - ev.Start;
        var newStart = new DateTimeOffset(move.NewStart, TimeZoneInfo.Local.GetUtcOffset(move.NewStart));
        var clash = _autopilotToday.Concat(_homeTomorrow).FirstOrDefault(e => e.Id != ev.Id && !e.IsAllDay && !e.IsNekomataManaged
            && e.Attendees.Count > 0 && e.Start < newStart + length && e.End > newStart);
        if (clash is not null)
        {
            ShowPaletteResponse($"That would land on {CleanCalendarObjectiveTitle(clash.Subject)}. Drop it somewhere free.");
            await RefreshHomeAsync();
            return;
        }
        var response = new GuardianActionResponse
        {
            ActionType = "ring_move",
            Message = "Moved a focus block from the ring.",
            Confidence = 100,
            Changes =
            [
                new GuardianChange
                {
                    EntityType = "Calendar", Property = "MoveNekomataBlock", OldValue = ev.Id,
                    NewValue = $"{newStart:O}|{newStart + length:O}", Reason = "Dragged on the home ring by the user.", Confidence = 100
                }
            ]
        };
        var result = await _guardianApplyService.ApplyAsync(response);
        if (result.Actions.Any(a => a.Operation == "Move"))
        {
            Think($"Moved {move.Item.Title} to {newStart.LocalDateTime:HH:mm}. Say undo to put it back.");
            _autopilotToday = await LoadAutopilotDayAsync(DateTimeOffset.Now);
        }
        else ShowPaletteResponse("That move didn't go through: " + string.Join(" ", result.Messages));
        await RefreshHomeAsync();
    }
}
