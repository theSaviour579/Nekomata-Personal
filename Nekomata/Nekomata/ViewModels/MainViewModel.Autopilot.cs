using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Autonomy;
using Nekomata.Core.Guardian.Autopilot;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// Day Autopilot: Guardian keeps its own focus blocks workable and keeps the user on them.
//
// Every minute it samples whether the user is working on the Guardian block that's running (a mission for that block).
// Ten minutes into a block he isn't on, it says so once: "start it" or "push it". A block that ends untouched is
// moved to later today. Every few minutes it also re-reads today's calendar and moves any future Guardian block that
// now clashes with something (a new meeting, an appointment, lunch) or runs past the working day.
//
// Only Guardian's own blocks are ever moved - never meetings or the user's own entries. Every move goes through the
// audited calendar path, is announced, and "undo" puts it back; an undone block is then left alone for the rest of
// the day. "Autopilot off" stops it (persisted), and it caps how often it moves things so it can't thrash.
public partial class MainViewModel
{
    private static readonly TimeSpan AutopilotNudgeAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AutopilotReplanInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AutopilotMissedWindow = TimeSpan.FromMinutes(30);
    private const int AutopilotMaxMovesPerBlockPerDay = 3;
    private const int AutopilotMaxMovesPerHour = 8;

    private sealed class BlockSamples { public int On; public int Off; public int Expected; }

    private readonly DispatcherTimer _autopilotTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Dictionary<string, BlockSamples> _autopilotSamples = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autopilotNudged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autopilotMissedHandled = new(StringComparer.Ordinal);
    private readonly HashSet<string> _autopilotHandsOff = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _autopilotMovesPerBlock = new(StringComparer.Ordinal);
    private readonly List<DateTimeOffset> _autopilotMoveTimes = [];
    private readonly Dictionary<Guid, List<string>> _autopilotBatches = [];
    private readonly Dictionary<string, DateTimeOffset> _autopilotProposedUntil = new(StringComparer.Ordinal);
    private DateTime _autopilotDay = DateTime.Today;
    private DateTimeOffset _autopilotLastReplan = DateTimeOffset.MinValue;
    private IReadOnlyList<CalendarEvent> _autopilotToday = [];
    private bool _autopilotBusy;

    [ObservableProperty] private string hudPlanText = "";
    [ObservableProperty] private bool hasHudPlan;
    [ObservableProperty] private bool hudOffPlan;

    private static string AutopilotSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "autopilot.json");

    public bool AutopilotEnabled => _autonomy.IsOn(AutonomyAction.MoveOwnBlocks);

    private void InitialiseAutopilot()
    {
        _autopilotTimer.Tick += (_, _) => _ = AutopilotTickAsync();
        _autopilotTimer.Start();
    }

    private async Task AutopilotTickAsync()
    {
        if (_autopilotBusy || IsInitialLoading) return;
        _autopilotBusy = true;
        try
        {
            var now = DateTimeOffset.Now;
            if (_autopilotDay != now.Date) ResetAutopilotDay(now.Date);
            if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { HasHudPlan = false; return; }

            if (now - _autopilotLastReplan >= AutopilotReplanInterval || _autopilotToday.Count == 0)
            {
                _autopilotToday = await LoadAutopilotDayAsync(now);
                _autopilotLastReplan = now;
            }

            var running = RunningGuardianBlock(now);
            SampleBlock(running, now);
            UpdatePlanHud(running, now);
            await NudgeIfOffPlanAsync(running, now);
            await RunSelfTuningIfDueAsync();
            var missed = _autonomy.IsOn(AutonomyAction.RescheduleMissedBlocks) ? FindMissedBlocks(now) : new HashSet<string>(StringComparer.Ordinal);
            if (missed.Count > 0 || now - _autopilotLastReplan < TimeSpan.FromSeconds(70))
                await ReplanAsync(now, missed, new HashSet<string>(), announceIfNothing: null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Autopilot tick failed safely: {ex.Message}");
        }
        finally
        {
            _autopilotBusy = false;
        }
    }

    private void ResetAutopilotDay(DateTime day)
    {
        _autopilotDay = day;
        _autopilotSamples.Clear();
        _autopilotNudged.Clear();
        _autopilotMissedHandled.Clear();
        _autopilotHandsOff.Clear();
        _autopilotProposedUntil.Clear();
        _autopilotMovesPerBlock.Clear();
        _autopilotToday = [];
    }

    private async Task<IReadOnlyList<CalendarEvent>> LoadAutopilotDayAsync(DateTimeOffset now)
    {
        var offset = TimeZoneInfo.Local.GetUtcOffset(now.Date);
        var start = new DateTimeOffset(now.Date, offset);
        return await _services.GetRequiredService<ICalendarService>().GetEventsAsync(start, start.AddDays(1));
    }

    private CalendarEvent? RunningGuardianBlock(DateTimeOffset now) => _autopilotToday
        .Where(e => e.IsNekomataManaged && !e.IsAllDay && e.Start <= now && e.End > now)
        .OrderBy(e => e.Start)
        .FirstOrDefault();

    private bool InMeeting(DateTimeOffset now) => _autopilotToday.Any(e =>
        !e.IsAllDay && !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start <= now && e.End > now);

    private void SampleBlock(CalendarEvent? block, DateTimeOffset now)
    {
        if (block is null) return;
        if (!_autopilotSamples.TryGetValue(block.Id, out var samples))
        {
            samples = new BlockSamples { Expected = Math.Max(1, (int)(block.End - block.Start).TotalMinutes) };
            _autopilotSamples[block.Id] = samples;
        }
        if (IsWorkingOnCalendarEvent(block))
        {
            samples.On++;
            if (_autopilotNudged.Contains(block.Id) && _nudgeFollowed.Add(block.Id)) LogAutopilot(AutopilotLogKind.NudgeFollowed, block.Id);
        }
        else samples.Off++;
    }

    private void UpdatePlanHud(CalendarEvent? running, DateTimeOffset now)
    {
        if (running is not null)
        {
            var on = IsWorkingOnCalendarEvent(running);
            var into = now - running.Start;
            HudOffPlan = !on && into >= NudgeAfter && !InMeeting(now);
            HudPlanText = on
                ? $"On plan · {CleanCalendarObjectiveTitle(running.Subject)} until {running.End.LocalDateTime:HH:mm}"
                : $"{(HudOffPlan ? "Off plan" : "Now")} · {CleanCalendarObjectiveTitle(running.Subject)}";
            HasHudPlan = !on; // when he's on it, the FOCUS segment already says so
            return;
        }
        HudOffPlan = false;
        var next = _autopilotToday.Where(e => e.IsNekomataManaged && !e.IsAllDay && e.Start > now)
            .OrderBy(e => e.Start).FirstOrDefault();
        HasHudPlan = next is not null;
        HudPlanText = next is null ? "" : $"Next block {next.Start.LocalDateTime:HH:mm} · {CleanCalendarObjectiveTitle(next.Subject)}";
    }

    private async Task NudgeIfOffPlanAsync(CalendarEvent? running, DateTimeOffset now)
    {
        if (!_autonomy.IsOn(AutonomyAction.OffPlanNudges)) return;
        if (running is null || _autopilotNudged.Contains(running.Id) || _awaySince is not null || InMeeting(now)) return;
        if (now - running.Start < NudgeAfter || running.End - now < TimeSpan.FromMinutes(10)) return;
        if (IsWorkingOnCalendarEvent(running)) return;
        _autopilotNudged.Add(running.Id);
        LogAutopilot(AutopilotLogKind.Nudged, running.Id);

        var title = CleanCalendarObjectiveTitle(running.Subject);
        var minutes = (int)(now - running.Start).TotalMinutes;
        Think($"You've drifted from the {title} block, {minutes} minutes in");
        var other = MissionActive && Workspace.CurrentMission is { } m ? $" You're on {m.Title} instead." : "";
        await SpeakGuardianAsync($"You're {minutes} minutes into the {SpokenTitle(title)} block.{other} Say start it, or push it and I'll move it later.");
    }

    private HashSet<string> FindMissedBlocks(DateTimeOffset now)
    {
        var missed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in _autopilotToday.Where(e => e.IsNekomataManaged && !e.IsAllDay && e.End <= now && now - e.End <= AutopilotMissedWindow))
        {
            if (_autopilotMissedHandled.Contains(block.Id) || _autopilotHandsOff.Contains(block.Id)) continue;
            if (!_autopilotSamples.TryGetValue(block.Id, out var samples)) continue; // the app wasn't watching
            // Only when Nekomata watched most of the block and the user was never on it.
            if (samples.On > 0 || samples.On + samples.Off < samples.Expected / 2) { _autopilotMissedHandled.Add(block.Id); continue; }
            if (ResolveCalendarTaskId(block) is long taskId &&
                Workspace.Tasks.Any(t => t.Id == taskId && (t.IsCompleted || t.Completed))) { _autopilotMissedHandled.Add(block.Id); continue; }
            missed.Add(block.Id);
        }
        return missed;
    }

    /// <summary>Plans and applies moves. announceIfNothing, when set, is said if there was nothing to do.</summary>
    private async Task<int> ReplanAsync(DateTimeOffset now, IReadOnlySet<string> missed, IReadOnlySet<string> push, string? announceIfNothing)
    {
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var dayStart = new DateTimeOffset(settings.GetStart(now.LocalDateTime.Date));
        var dayEnd = new DateTimeOffset(settings.GetEnd(now.LocalDateTime.Date));
        var events = _autopilotToday.Select(e => new AutopilotEvent(e.Id, CleanCalendarObjectiveTitle(e.Subject), e.Start, e.End, e.IsAllDay, e.IsNekomataManaged)).ToList();
        if (settings.IncludeLunchBreak)
            events.Add(new AutopilotEvent("lunch", "lunch", new DateTimeOffset(settings.GetLunchStart(now.LocalDateTime.Date)),
                new DateTimeOffset(settings.GetLunchEnd(now.LocalDateTime.Date)), false, false));

        // Blocks that have already been moved too often today are left for the user.
        var handsOff = new HashSet<string>(_autopilotHandsOff, StringComparer.Ordinal);
        foreach (var (id, count) in _autopilotMovesPerBlock) if (count >= AutopilotMaxMovesPerBlockPerDay && !push.Contains(id)) handsOff.Add(id);
        foreach (var (id, until) in _autopilotProposedUntil) if (until > now && !push.Contains(id)) handsOff.Add(id); // waiting on the user
        if (!_autonomy.IsOn(AutonomyAction.MoveOwnBlocks))
            foreach (var block in _autopilotToday.Where(e => e.IsNekomataManaged && !missed.Contains(e.Id) && !push.Contains(e.Id)))
                handsOff.Add(block.Id);

        var plan = DayAutopilotPlanner.Replan(events, now, dayStart, dayEnd, missed, push, handsOff,
            new AutopilotOptions { AvoidHours = _autopilotProfile.AvoidHours.ToHashSet() });
        foreach (var id in missed) _autopilotMissedHandled.Add(id);
        _autopilotMoveTimes.RemoveAll(t => now - t > TimeSpan.FromHours(1));
        var moves = plan.Moves.Take(Math.Max(0, AutopilotMaxMovesPerHour - _autopilotMoveTimes.Count)).ToList();
        if (push.Count > 0) moves = plan.Moves.ToList(); // an explicit request is never rate limited

        var unplaced = plan.Unplaced.Where(u => missed.Contains(u.EventId) || push.Contains(u.EventId) ||
                                                _autopilotMissedHandled.Add("unplaced:" + u.EventId)).ToList();
        if (moves.Count == 0)
        {
            if (unplaced.Count > 0)
                await SpeakGuardianAsync($"There's no room left today for {SpokenList(unplaced.Select(u => SpokenTitle(u.Subject)))}. Say plan tomorrow and I'll fit {(unplaced.Count == 1 ? "it" : "them")} in.");
            else if (announceIfNothing is not null)
                await SpeakGuardianAsync(announceIfNothing);
            return 0;
        }

        Think(moves.Count == 1
            ? $"{moves[0].Subject} needs a new slot: {moves[0].Reason}"
            : $"Reshuffling {moves.Count} focus blocks around changes in your calendar");
        // Ask-mode moves are proposed and wait for "do it"; an explicit push is always just done.
        var ask = moves.Where(m => !push.Contains(m.EventId) &&
            _autonomy.Get(missed.Contains(m.EventId) ? AutonomyAction.RescheduleMissedBlocks : AutonomyAction.MoveOwnBlocks) == AutonomyMode.Ask).ToList();
        if (ask.Count > 0)
        {
            foreach (var m in ask) _autopilotProposedUntil[m.EventId] = now + PendingAutonomyLifetime;
            SetPendingAutonomyAction("rearrange your focus blocks", async () =>
            {
                _autopilotToday = await LoadAutopilotDayAsync(DateTimeOffset.Now);
                foreach (var m in ask) _autopilotProposedUntil.Remove(m.EventId);
                await ApplyAutopilotMovesAsync(DateTimeOffset.Now, ask, []);
            });
            var proposal = ask.Count == 1
                ? $"{SpokenTitle(ask[0].Subject)} needs moving, because {ask[0].Reason}. I'd put it at {ask[0].NewStart.LocalDateTime:HH:mm}. Say do it."
                : $"{ask.Count} focus blocks need moving: {SpokenList(ask.Select(m => $"{SpokenTitle(m.Subject)} to {m.NewStart.LocalDateTime:HH:mm}"))}. Say do it.";
            ShowPaletteResponse(proposal);
            await SpeakGuardianAsync(proposal);
            moves = moves.Except(ask).ToList();
            if (moves.Count == 0) return 0;
        }
        return await ApplyAutopilotMovesAsync(now, moves, unplaced);
    }

    private async Task<int> ApplyAutopilotMovesAsync(DateTimeOffset now, IReadOnlyList<AutopilotMove> moves, IReadOnlyList<AutopilotUnplaced> unplaced)
    {
        var response = new GuardianActionResponse
        {
            ActionType = "day_autopilot",
            Message = "Day Autopilot kept Guardian's focus blocks workable.",
            Confidence = 95,
            Changes = moves.Select(m => new GuardianChange
            {
                EntityType = "Calendar",
                Property = "MoveNekomataBlock",
                OldValue = m.EventId,
                NewValue = $"{m.NewStart:O}|{m.NewEnd:O}",
                Reason = $"Day Autopilot: moved because {m.Reason}.",
                Confidence = 95
            }).ToList()
        };
        var result = await _guardianApplyService.ApplyAsync(response);
        var movedIds = result.Actions.Where(a => a.Operation == "Move" && !string.IsNullOrWhiteSpace(a.ExternalId))
            .Select(a => a.ExternalId!).ToHashSet(StringComparer.Ordinal);
        var applied = moves.Where(m => movedIds.Contains(m.EventId)).ToList();
        if (applied.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("Autopilot moves were not applied: " + string.Join(" ", result.Messages));
            return 0;
        }

        foreach (var m in applied)
        {
            LogAutopilot(AutopilotLogKind.Moved, m.EventId, m.OldStart.LocalDateTime, m.NewStart.LocalDateTime);
            _lastAutopilotMoves[m.EventId] = (m.OldStart.LocalDateTime, m.NewStart.LocalDateTime);
            _autopilotMovesPerBlock[m.EventId] = _autopilotMovesPerBlock.GetValueOrDefault(m.EventId) + 1;
            _autopilotMoveTimes.Add(now);
        }
        if (result.AuditBatchId is { } batch) _autopilotBatches[batch] = applied.Select(m => m.EventId).ToList();

        _autopilotToday = await LoadAutopilotDayAsync(now);
        _autopilotLastReplan = now;
        _ = RefreshCalendarAwareObjectiveAsync();
        if (SelectedCalendarDate.Date == DateTime.Today) _ = RefreshCalendarAsync();

        Think("Moved " + SpokenList(applied.Select(m => $"{m.Subject} to {m.NewStart.LocalDateTime:HH:mm}")));
        foreach (var m in applied) RecordDone($"Moved {m.Subject} to {m.NewStart.LocalDateTime:HH:mm} because {m.Reason}.");
        await SpeakGuardianAsync(DescribeAutopilotMoves(applied, unplaced));
        return applied.Count;
    }

    private static string DescribeAutopilotMoves(IReadOnlyList<AutopilotMove> moves, IReadOnlyList<AutopilotUnplaced> unplaced)
    {
        string Where(AutopilotMove m) => $"{m.NewStart.LocalDateTime:HH:mm}{(m.Shortened ? $", shortened to {(int)(m.NewEnd - m.NewStart).TotalMinutes} minutes" : "")}";
        var text = moves.Count == 1
            ? $"I've moved {SpokenTitle(moves[0].Subject)} to {Where(moves[0])}, because {moves[0].Reason}."
            : $"I've rearranged your day: {SpokenList(moves.Select(m => $"{SpokenTitle(m.Subject)} to {Where(m)}"))}.";
        if (unplaced.Count > 0)
            text += $" There's no room today for {SpokenList(unplaced.Select(u => SpokenTitle(u.Subject)))}.";
        return text + (moves.Count == 1 ? " Say undo to put it back." : " Say undo to put them back.");
    }

    private static string SpokenList(IEnumerable<string> items)
    {
        var list = items.ToArray();
        return list.Length switch { 0 => "", 1 => list[0], _ => string.Join(", ", list[..^1]) + " and " + list[^1] };
    }

    // ---- Commands ----

    private async Task StartCurrentBlockAsync()
    {
        var now = DateTimeOffset.Now;
        _autopilotToday = await LoadAutopilotDayAsync(now);
        var block = RunningGuardianBlock(now);
        if (block is null)
        {
            ShowPaletteResponse("There's no Guardian block running right now.");
            return;
        }
        if (IsWorkingOnCalendarEvent(block)) { ShowPaletteResponse("You're already on it."); return; }
        if (MissionActive)
        {
            // Switching would abandon the session in progress; let the user close it his way first.
            var message = $"You're still on {Workspace.CurrentMission?.Title ?? "another mission"}. Finish it or hold it for later, then say start it.";
            ShowPaletteResponse(message);
            await SpeakGuardianAsync(message, true);
            return;
        }
        SetActiveFocusObjective(block, now);
        OnPropertyChanged(nameof(Workspace));
        await BeginMissionAsync();
        _autopilotNudged.Add(block.Id);
    }

    private async Task PushCurrentBlockAsync()
    {
        var now = DateTimeOffset.Now;
        _autopilotToday = await LoadAutopilotDayAsync(now);
        var block = RunningGuardianBlock(now) ?? _autopilotToday
            .Where(e => e.IsNekomataManaged && !e.IsAllDay && e.Start > now && e.Start - now <= TimeSpan.FromMinutes(30))
            .OrderBy(e => e.Start).FirstOrDefault();
        if (block is null) { ShowPaletteResponse("There's no Guardian block now or in the next half hour to push."); return; }
        if (IsWorkingOnCalendarEvent(block) && MissionActive)
        {
            ShowPaletteResponse("You're working on that block. Finish it, or say you need more time.");
            return;
        }
        _autopilotHandsOff.Remove(block.Id);
        if (_autopilotNudged.Contains(block.Id) && _nudgeFollowed.Add(block.Id)) LogAutopilot(AutopilotLogKind.NudgeFollowed, block.Id);
        _autopilotNudged.Add(block.Id);
        _autopilotMissedHandled.Add(block.Id);
        var moved = await ReplanAsync(now, new HashSet<string>(), new HashSet<string> { block.Id }, announceIfNothing: null);
        if (moved == 0) ShowPaletteResponse($"I couldn't find anywhere later today for {CleanCalendarObjectiveTitle(block.Subject)}. Say plan tomorrow to fit it in.");
    }

    private async Task SetAutopilotAsync(bool enabled)
    {
        SetAutonomy(AutonomyAction.MoveOwnBlocks, enabled ? AutonomyMode.Auto : AutonomyMode.Off);
        SetAutonomy(AutonomyAction.RescheduleMissedBlocks, enabled ? AutonomyMode.Auto : AutonomyMode.Off);
        var message = enabled
            ? "Autopilot is on. I'll keep my focus blocks workable and keep you on them. Your meetings are never moved."
            : "Autopilot is off. I won't move anything in your calendar until you turn it back on.";
        ShowPaletteResponse(message);
        await SpeakGuardianAsync(message, true);
        if (enabled) { _autopilotLastReplan = DateTimeOffset.MinValue; _ = AutopilotTickAsync(); }
    }

    private async Task ShowPlanStatusAsync()
    {
        var now = DateTimeOffset.Now;
        _autopilotToday = await LoadAutopilotDayAsync(now);
        var blocks = _autopilotToday.Where(e => e.IsNekomataManaged && !e.IsAllDay).OrderBy(e => e.Start).ToList();
        var meetings = _autopilotToday.Where(e => !e.IsNekomataManaged && !e.IsAllDay && e.End > now).OrderBy(e => e.Start).ToList();
        var running = RunningGuardianBlock(now);
        var parts = new List<string>();

        if (running is not null)
            parts.Add(IsWorkingOnCalendarEvent(running)
                ? $"You're on plan: {SpokenTitle(CleanCalendarObjectiveTitle(running.Subject))} until {running.End.LocalDateTime:HH:mm}."
                : $"Right now you should be on {SpokenTitle(CleanCalendarObjectiveTitle(running.Subject))}, until {running.End.LocalDateTime:HH:mm}, and you're not on it yet.");
        else if (meetings.FirstOrDefault(m => m.Start <= now) is { } inMeeting)
            parts.Add($"You're in {SpokenTitle(CleanCalendarObjectiveTitle(inMeeting.Subject))} until {inMeeting.End.LocalDateTime:HH:mm}.");

        var done = blocks.Where(b => b.End <= now).ToList();
        var skipped = done.Where(b => _autopilotSamples.TryGetValue(b.Id, out var s) && s.On == 0).ToList();
        if (done.Count > 0)
            parts.Add(skipped.Count == 0
                ? $"{done.Count} focus block{(done.Count == 1 ? "" : "s")} behind you today."
                : $"{done.Count} focus block{(done.Count == 1 ? "" : "s")} behind you, {skipped.Count} of them missed.");

        var upcoming = blocks.Where(b => b.Start > now).ToList();
        if (upcoming.Count > 0)
            parts.Add($"Still to come: {SpokenList(upcoming.Take(3).Select(b => $"{SpokenTitle(CleanCalendarObjectiveTitle(b.Subject))} at {b.Start.LocalDateTime:HH:mm}"))}" +
                      (upcoming.Count > 3 ? $", and {upcoming.Count - 3} more." : "."));
        var nextMeeting = meetings.FirstOrDefault(m => m.Start > now && m.Attendees.Count > 0);
        if (nextMeeting is not null)
            parts.Add($"Next meeting: {SpokenTitle(CleanCalendarObjectiveTitle(nextMeeting.Subject))} at {nextMeeting.Start.LocalDateTime:HH:mm}.");
        if (blocks.Count == 0)
            parts.Add("There are no Guardian focus blocks in your calendar today. Say plan my day and I'll lay some out.");
        if (!AutopilotEnabled) parts.Add("Autopilot is off.");

        var message = string.Join(" ", parts);
        ShowPaletteResponse(message);
        await SpeakGuardianAsync(message, true);
    }

    /// <summary>Called after "undo": an undone autopilot move means the user wants that block where it is.</summary>
    private void NoteAutopilotUndo(Guid batchId)
    {
        if (!_autopilotBatches.Remove(batchId, out var ids)) return;
        foreach (var id in ids)
            if (_lastAutopilotMoves.TryGetValue(id, out var move)) LogAutopilot(AutopilotLogKind.Undone, id, move.From, move.To);
        _autopilotHandsOff.UnionWith(ids);
        _autopilotLastReplan = DateTimeOffset.MinValue;
        if (SelectedCalendarDate.Date == DateTime.Today) _ = RefreshCalendarAsync();
        _ = RefreshCalendarAwareObjectiveAsync();
    }
}
