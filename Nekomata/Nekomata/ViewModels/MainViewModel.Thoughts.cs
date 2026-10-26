using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nekomata.UI.ViewModels;

// "What's Guardian thinking?" - one short line under the greeting (and on the heads-up). Background work says what
// it's doing as it does it ("Reading 4 sent emails for promises", "Ops review landed on a focus block - finding it a new
// slot"); after a minute and a half the line falls back to an ambient summary of what Guardian is watching (the
// block you're in, the next meeting, the nearest task deadline, anything held while you're on a call, anything
// waiting for your go-ahead). Hovering shows the last dozen thoughts with times.
public partial class MainViewModel
{
    private static readonly TimeSpan ThoughtHold = TimeSpan.FromSeconds(90);
    private const int ThoughtHistoryLength = 12;

    private readonly DispatcherTimer _thoughtTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly List<(DateTimeOffset At, string Text)> _thoughts = [];
    private DateTimeOffset _thoughtHeldUntil = DateTimeOffset.MinValue;

    [ObservableProperty] private string guardianThought = "Getting my bearings…";
    [ObservableProperty] private string guardianThoughtHistory = "Nothing yet.";

    private void InitialiseThoughts()
    {
        _thoughtTimer.Tick += (_, _) => RefreshAmbientThought();
        _thoughtTimer.Start();
    }

    /// <summary>Say what Guardian is doing right now. Safe from any thread.</summary>
    private void Think(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Think(text));
            return;
        }
        text = text.Trim();
        if (text.Length > 140) text = text[..140].TrimEnd() + "…";
        _thoughtHeldUntil = DateTimeOffset.Now + ThoughtHold;
        GuardianThought = text;
        if (_thoughts.Count > 0 && _thoughts[^1].Text == text) return;
        _thoughts.Add((DateTimeOffset.Now, text));
        if (_thoughts.Count > ThoughtHistoryLength) _thoughts.RemoveAt(0);
        GuardianThoughtHistory = string.Join(Environment.NewLine, _thoughts.AsEnumerable().Reverse().Select(t => $"{t.At.LocalDateTime:HH:mm}  {t.Text}"));
    }

    private void RefreshAmbientThought()
    {
        try
        {
            if (DateTimeOffset.Now < _thoughtHeldUntil || IsInitialLoading) return;
            GuardianThought = AmbientThought();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ambient thought failed safely: {ex.Message}");
        }
    }

    private string AmbientThought()
    {
        var now = DateTimeOffset.Now;
        var parts = new List<string>();

        if (_pendingAutonomy is { } pending && pending.Expires > now)
            return $"Waiting for your go-ahead to {pending.Description}. Say do it.";
        if (SpeechHoldReason() is { } reason)
            parts.Add(_heldSpeech.Count > 0 ? $"Keeping quiet ({reason}), {_heldSpeech.Count} to tell you after" : $"Keeping quiet ({reason})");

        var running = RunningGuardianBlock(now);
        if (running is not null)
            parts.Add(IsWorkingOnCalendarEvent(running)
                ? $"Guarding your {CleanCalendarObjectiveTitle(running.Subject)} block until {running.End.LocalDateTime:HH:mm}"
                : $"You should be on {CleanCalendarObjectiveTitle(running.Subject)} until {running.End.LocalDateTime:HH:mm}");

        var meeting = _autopilotToday
            .Where(e => !e.IsAllDay && !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start > now && e.Start - now <= TimeSpan.FromMinutes(60))
            .OrderBy(e => e.Start).FirstOrDefault();
        if (meeting is not null)
        {
            var minutes = (int)Math.Ceiling((meeting.Start - now).TotalMinutes);
            parts.Add(minutes > 5
                ? $"{CleanCalendarObjectiveTitle(meeting.Subject)} in {minutes} min, I'll brief you 5 minutes before"
                : $"{CleanCalendarObjectiveTitle(meeting.Subject)} in {minutes} min");
        }

        var localNow = DateTime.Now;
        var dueSoon = Workspace.Tasks
            .Where(x => !x.Completed && x.DueAt is not null && x.DueAt.Value - localNow <= TimeSpan.FromHours(2))
            .OrderBy(x => x.DueAt).FirstOrDefault();
        if (dueSoon is not null)
        {
            var left = (int)Math.Round((dueSoon.DueAt!.Value - localNow).TotalMinutes);
            var id = $"'{Trim(dueSoon.Title, 40)}'";
            parts.Add(left >= 0 ? $"watching {id}, due in {left} min" : $"{id} is overdue");
        }

        if (parts.Count == 0)
        {
            if (QuietLine() is { } quip) return quip;
            var next = _autopilotToday.Where(e => e.IsNekomataManaged && !e.IsAllDay && e.Start > now).OrderBy(e => e.Start).FirstOrDefault();
            return next is not null
                ? $"All quiet. Next focus block: {CleanCalendarObjectiveTitle(next.Subject)} at {next.Start.LocalDateTime:HH:mm}."
                : "All quiet. Watching your calendar, inbox and tasks.";
        }
        var text = string.Join(" · ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }
}
