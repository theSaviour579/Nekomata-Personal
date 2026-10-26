using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// A bit of personality, kept honest. Guardian greets the user the first time it sees them each working day and signs
// off once the day is over; the core's glow turns amber when they are off plan; a light line now and then when there
// is genuinely nothing going on (never when something needs them); it thinks out loud while working on a question;
// and it celebrates, briefly, when the overdue list clears.
public partial class MainViewModel
{
    private static readonly Color CoreAmber = Color.FromRgb(0xFF, 0xC8, 0x57);

    private static readonly string[] QuietLines =
    [
        "Nothing on fire. Suspicious.",
        "All quiet. Even the inbox is behaving.",
        "No alarms. Enjoy it while it lasts.",
        "Everything's calm. I'm keeping one eye open.",
        "Quiet on all fronts. Good moment for the thing you've been putting off?",
        "Nothing needs you right now. I'm still watching.",
        "Calendar sane, inbox calm. A rare combination."
    ];

    private DateTime _greetedOn = DateTime.MinValue;
    private DateTime _signedOffOn = DateTime.MinValue;
    private int? _lastOverdueTasks;
    private readonly DispatcherTimer _narration = new() { Interval = TimeSpan.FromMilliseconds(1700) };
    private readonly Queue<string> _narrationSteps = new();
    private bool _narrationHooked;

    /// <summary>Goes up each time there is something to celebrate; the core answers with green ripples.</summary>
    [ObservableProperty] private int homeCelebrateCount;

    private string FirstName()
    {
        return UserFirstName.Length == 0 ? "there" : UserFirstName;
    }

    /// <summary>Mood: an amber core when the day is off plan (calm purple otherwise).</summary>
    private void ApplyHomeMood()
    {
        if (_homeNowKind is "mission" or "meeting" or "evening") return;
        if (HudOffPlan) HomeCoreAccent = CoreAmber;
    }

    private void RunPersonalityBeats(List<CalendarEvent> events, DateTimeOffset now, WorkingDaySettings settings)
    {
        var today = now.Date;
        var workday = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        // Morning (or first-thing) greeting, once a working day.
        if (workday && _greetedOn != today && now.LocalDateTime < settings.GetEnd(today) && !IsInitialLoading)
        {
            _greetedOn = today;
            var hello = now.Hour < 12 ? "Morning" : "Afternoon";
            var needs = VisibleAttentionItems.Count;
            var nextMeeting = events.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0 && e.Start > now).OrderBy(e => e.Start).FirstOrDefault();
            var meetingText = nextMeeting is null ? "no more meetings today"
                : $"next meeting {CleanCalendarObjectiveTitle(nextMeeting.Subject)} at {nextMeeting.Start.LocalDateTime:HH:mm}";
            var greeting = $"{hello}, {FirstName()}. {(needs == 0 ? "Nothing needs you yet" : needs == 1 ? "1 thing needs you" : $"{needs} things need you")}, {meetingText}.";
            Think(greeting);
            // The start-up moment: the ring draws itself in while Guardian says hello.
            HomeIntroCount++;
            _ = SpeakGuardianAsync(greeting);
        }

        // Sign-off, once the working day is over.
        if (workday && _homeNowKind == "evening" && _signedOffOn != today && now.LocalDateTime >= settings.GetEnd(today))
        {
            _signedOffOn = today;
            var focus = events.Where(e => e.IsNekomataManaged).Sum(e => (e.End - e.Start).TotalMinutes);
            var done = HomeDoneTotal;
            var first = _homeTomorrow.Where(e => !e.IsAllDay).OrderBy(e => e.Start).FirstOrDefault();
            Think($"Day closed. {(int)focus / 60} h {(int)focus % 60:00} of focus booked, {done} thing{(done == 1 ? "" : "s")} done for you." +
                  (first is null ? " Nothing booked for tomorrow yet." : $" Tomorrow starts {first.Start.LocalDateTime:HH:mm} with {CleanCalendarObjectiveTitle(first.Subject)}."));
        }

        // Celebrations, when the overdue list empties.
        var overdue = Workspace.Tasks.Count(t => !t.Completed && t.DueAt is { } due && due < now.LocalDateTime);
        if (_lastOverdueTasks is > 0 && overdue == 0) Celebrate("No overdue tasks left. Nicely done.");
        _lastOverdueTasks = overdue;
    }

    private void Celebrate(string text)
    {
        Think(text);
        HomeCelebrateCount++;
    }

    /// <summary>Now and then, when there is truly nothing going on, a light line instead of "All quiet".</summary>
    private string? QuietLine()
    {
        if (VisibleAttentionItems.Count > 0 || HudOffPlan) return null;
        var slot = (int)(DateTime.Now.TimeOfDay.TotalMinutes / 10);
        return slot % 3 == 0 ? QuietLines[slot / 3 % QuietLines.Length] : null;
    }

    // Thinking out loud: while Guardian works on a question, the thought line steps through what it is looking at,
    // based on what was asked.
    partial void OnGuardianBusyChanged(bool value)
    {
        if (!_narrationHooked)
        {
            _narrationHooked = true;
            _narration.Tick += (_, _) =>
            {
                if (_narrationSteps.Count == 0) { _narration.Stop(); return; }
                Think(_narrationSteps.Dequeue());
            };
        }
        _narration.Stop();
        _narrationSteps.Clear();
        if (!value) return;

        var asked = (ChatHistory.LastOrDefault(t => t.IsUser)?.Content ?? "").ToLowerInvariant();
        bool Has(params string[] words) => words.Any(asked.Contains);
        var steps = new List<string> { "Reading your question…" };
        if (Has("calendar", "meeting", "today", "tomorrow", "week", "plan", "schedule", "diary", "free")) steps.Add("Checking your calendar…");
        if (Has("task", "to do", "todo", "overdue", "due")) steps.Add("Checking your tasks…");
        if (Has("email", "mail", "inbox", "reply", "sent")) steps.Add("Going through your inbox…");
        if (Has("project")) steps.Add("Looking at your projects…");
        if (Has("teams", "chat", "message")) steps.Add("Looking through Teams…");
        steps.Add("Weighing it up…");
        steps.Add("Putting the answer together…");
        Think(steps[0]);
        foreach (var step in steps.Skip(1)) _narrationSteps.Enqueue(step);
        _narration.Start();
    }
}
