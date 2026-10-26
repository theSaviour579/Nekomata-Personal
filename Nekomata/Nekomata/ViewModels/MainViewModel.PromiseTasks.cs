using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Autonomy;
using Nekomata.Core.Guardian.Autopilot;
using Nekomata.Integrations.MicrosoftGraph.Calendar;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// Promises become tasks. Every 20 minutes of the working day, if the user has sent new email, the sent-mail promise
// analysis runs and each new commitment becomes a task with the date he gave - and, when it's due by tomorrow, a
// focus block in the first free slot before then. One audited batch per run, announced, "undo" removes it.
// Each promise is handled once (remembered per day on disk), and one that already matches an open task is skipped.
// Autonomy: Auto does it and says so; Ask proposes and waits for "do it"; Off does nothing.
public partial class MainViewModel
{
    private static readonly TimeSpan PromiseTaskInterval = TimeSpan.FromMinutes(20);
    private readonly DispatcherTimer _promiseTaskTimer = new() { Interval = PromiseTaskInterval };
    private bool _promiseTasksBusy;

    private sealed class PromiseTaskLedger
    {
        public DateTime Day { get; set; }
        public List<string> Handled { get; set; } = [];
    }

    private static string PromiseLedgerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "promise-tasks.json");

    private void InitialisePromiseTasks()
    {
        _promiseTaskTimer.Tick += (_, _) => _ = TurnPromisesIntoTasksAsync();
        _promiseTaskTimer.Start();
    }

    private async Task TurnPromisesIntoTasksAsync()
    {
        if (_promiseTasksBusy || IsInitialLoading) return;
        var mode = _autonomy.Get(AutonomyAction.PromisesToTasks);
        if (mode == AutonomyMode.Off) return;
        var now = DateTimeOffset.Now;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        if (now.LocalDateTime < settings.GetStart(now.Date) || now.LocalDateTime > settings.GetEnd(now.Date).AddHours(1)) return;

        _promiseTasksBusy = true;
        try
        {
            var summary = await ComposePromisesTodayAsync(TimeSpan.Zero);
            var ledger = LoadPromiseLedger(now.Date);
            var openTitles = Workspace.Tasks.Where(t => !t.IsCompleted && !t.Completed).Select(t => SignificantWords(t.Title)).ToList();
            var fresh = summary.Items
                .Where(i => !ledger.Handled.Contains(PromiseKey(i)))
                .Where(i => !openTitles.Any(words => Overlap(SignificantWords(i.What), words) >= 0.6))
                .ToList();
            // Anything seen is handled, whether or not it becomes a task - a skipped duplicate shouldn't come back.
            ledger.Handled.AddRange(summary.Items.Select(PromiseKey).Where(k => !ledger.Handled.Contains(k)));
            SavePromiseLedger(ledger);
            if (fresh.Count == 0) return;

            if (mode == AutonomyMode.Ask)
            {
                var list = SpokenList(fresh.Select(i => i.What));
                SetPendingAutonomyAction($"add {(fresh.Count == 1 ? "a task" : $"{fresh.Count} tasks")} from your email promises",
                    () => CreatePromiseTasksAsync(fresh));
                await SpeakGuardianAsync($"From your sent emails: {list}. Want {(fresh.Count == 1 ? "it as a task" : "them as tasks")}? Say do it.");
                return;
            }
            await CreatePromiseTasksAsync(fresh);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Promise tasks failed safely: {ex.Message}");
        }
        finally
        {
            _promiseTasksBusy = false;
        }
    }

    private async Task CreatePromiseTasksAsync(IReadOnlyList<PromiseItem> promises)
    {
        Think($"Turning {promises.Count} email promise{(promises.Count == 1 ? "" : "s")} into tasks and looking for time to do {(promises.Count == 1 ? "it" : "them")}");
        var now = DateTimeOffset.Now;
        var response = new GuardianActionResponse
        {
            ActionType = "promise_tasks",
            Message = "Tasks from commitments the user made by email.",
            Confidence = 90
        };
        var booked = new List<(PromiseItem Item, DateTimeOffset Start)>();
        var book = _autonomy.Get(AutonomyAction.BookTimeForPromises) == AutonomyMode.Auto;
        var claimed = new List<AutopilotEvent>();

        for (var index = 0; index < promises.Count; index++)
        {
            var p = promises[index];
            response.Tasks.Add(new ProposedTask
            {
                Title = p.What,
                Description = $"Promised to {(p.To.Length > 0 ? p.To : "someone")} by email on {p.SentAt.LocalDateTime:ddd dd MMM HH:mm} (\"{p.Subject}\"). Added by Guardian.",
                Priority = p.Due is { } d && d - now.LocalDateTime < TimeSpan.FromDays(1) ? "High" : "Normal",
                EstimatedMinutes = p.EstimatedMinutes,
                DueAt = p.Due,
                RequiresFocus = p.EstimatedMinutes >= 30
            });

            if (!book || p.Due is not { } due || due.Date > WrapUpPlanningNextWorkday(now.Date)) continue;
            var slot = await FindPromiseSlotAsync(now, new DateTimeOffset(due), TimeSpan.FromMinutes(p.EstimatedMinutes), claimed);
            if (slot is not { } s) continue;
            claimed.Add(new AutopilotEvent($"claimed-{index}", p.What, s.Start, s.End, false, true));
            booked.Add((p, s.Start));
            response.Changes.Add(new GuardianChange
            {
                EntityType = "Calendar",
                Property = "CreateFocusBlock",
                EntityId = -(index + 1),
                NewValue = $"{s.Start:O}|{s.End:O}|{p.What}",
                Reason = $"Time for a promise to {p.To}, due {p.Due:ddd HH:mm}.",
                Confidence = 90
            });
        }

        var result = await _guardianApplyService.ApplyAsync(response);
        if (result.CreatedTaskIds.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("Promise tasks not created: " + string.Join(" ", result.Messages));
            return;
        }
        Workspace = await _workspaceCoordinator.RefreshAsync();
        if (booked.Count > 0)
        {
            _autopilotLastReplan = DateTimeOffset.MinValue;
            _ = RefreshCalendarAwareObjectiveAsync();
            if (SelectedCalendarDate.Date == DateTime.Today) _ = RefreshCalendarAsync();
        }

        var described = promises.Select(p =>
        {
            var slot = booked.FirstOrDefault(b => b.Item == p);
            var due = p.Due is { } d ? $", due {DescribeDue(d, now.LocalDateTime).Replace("by ", "")}" : "";
            var when = slot.Item is null ? "" : $", booked {(slot.Start.Date == now.Date ? "" : "tomorrow ")}at {slot.Start.LocalDateTime:HH:mm}";
            return $"{p.What}{due}{when}";
        });
        var message = promises.Count == 1
            ? $"From your sent email I've added a task: {described.First()}. Say undo to remove it."
            : $"From your sent emails I've added {promises.Count} tasks: {SpokenList(described)}. Say undo to remove them.";
        ShowPaletteResponse(message);
        RecordDone(message.Replace(" Say undo to remove it.", "").Replace(" Say undo to remove them.", ""));
        await SpeakGuardianAsync(message);
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End)?> FindPromiseSlotAsync(
        DateTimeOffset now, DateTimeOffset due, TimeSpan duration, IReadOnlyList<AutopilotEvent> claimed)
    {
        var settings = _services.GetRequiredService<WorkingDaySettings>();
        var calendar = _services.GetRequiredService<ICalendarService>();
        for (var day = now.Date; day <= due.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var offset = TimeZoneInfo.Local.GetUtcOffset(day);
            var start = new DateTimeOffset(day, offset);
            var events = (await calendar.GetEventsAsync(start, start.AddDays(1)))
                .Select(e => new AutopilotEvent(e.Id, e.Subject, e.Start, e.End, e.IsAllDay, e.IsNekomataManaged))
                .Concat(claimed)
                .ToList();
            if (settings.IncludeLunchBreak)
                events.Add(new AutopilotEvent("lunch", "lunch", new DateTimeOffset(settings.GetLunchStart(day)), new DateTimeOffset(settings.GetLunchEnd(day)), false, false));
            var slot = DayAutopilotPlanner.FindSlot(events, now, due, new DateTimeOffset(settings.GetStart(day)), new DateTimeOffset(settings.GetEnd(day)), duration);
            if (slot is not null) return slot;
        }
        return null;
    }

    private static DateTime WrapUpPlanningNextWorkday(DateTime today) =>
        Nekomata.Core.Guardian.Outcomes.WrapUpPlanning.NextWorkday(today);

    private static string PromiseKey(PromiseItem item) => $"{item.To.ToLowerInvariant()}|{string.Join(' ', SignificantWords(item.What).Order())}";

    private static double Overlap(HashSet<string> a, HashSet<string> b) =>
        a.Count == 0 || b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / Math.Min(a.Count, b.Count);

    private static PromiseTaskLedger LoadPromiseLedger(DateTime day)
    {
        try
        {
            if (File.Exists(PromiseLedgerPath))
            {
                var ledger = JsonSerializer.Deserialize<PromiseTaskLedger>(File.ReadAllText(PromiseLedgerPath));
                if (ledger is not null && ledger.Day == day) return ledger;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Promise ledger unreadable: {ex.Message}"); }
        return new PromiseTaskLedger { Day = day };
    }

    private static void SavePromiseLedger(PromiseTaskLedger ledger)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PromiseLedgerPath)!);
            File.WriteAllText(PromiseLedgerPath, JsonSerializer.Serialize(ledger));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Promise ledger not saved: {ex.Message}"); }
    }
}
