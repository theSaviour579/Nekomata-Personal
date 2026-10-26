using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Models;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

public sealed record HomeNextItem(string Time, string Title);

// The Guardian core and the signals line on the HUD home page. The core is driven by the same "now" logic as before
// (_homeNowKind): a focus block counts down in purple, a meeting in cyan, free time shows when it ends, a running
// mission turns the core cyan and dims the rest of the page, and in the evening the ring becomes tomorrow.
public partial class MainViewModel
{
    private static readonly Color CorePurple = Color.FromRgb(0x9C, 0x85, 0xFF);
    private static readonly Color CoreCyan = Color.FromRgb(0x42, 0xD9, 0xFF);

    [ObservableProperty] private string homeCoreLabel = "GUARDIAN";
    [ObservableProperty] private string homeCoreBig = "--:--";
    [ObservableProperty] private string homeCoreSub = "";
    [ObservableProperty] private double homeCoreProgress;
    [ObservableProperty] private Color homeCoreAccent = CorePurple;
    [ObservableProperty] private bool homeCoreShowNow = true;
    [ObservableProperty] private DateTime homeCoreDayStart = DateTime.Today.AddHours(8);
    [ObservableProperty] private DateTime homeCoreDayEnd = DateTime.Today.AddHours(17);
    [ObservableProperty] private bool homeInFocus;
    [ObservableProperty] private int homeDeskActive;
    [ObservableProperty] private int homeDeskBreached;
    [ObservableProperty] private string homeDeskFlow = "";
    [ObservableProperty] private string homeSystemsHealthy = "–";
    [ObservableProperty] private string homeSystemsNote = "";
    [ObservableProperty] private bool homeSystemsWarn;

    public ObservableCollection<HomeTimelineItem> HomeCoreItems { get; } = [];
    public ObservableCollection<HomeNextItem> HomeNext { get; } = [];

    private void RefreshHomeCore(List<CalendarEvent> events, DateTimeOffset now, WorkingDaySettings settings)
    {
        HomeInFocus = _homeNowKind == "mission";
        HomeCoreShowNow = _homeNowKind != "evening";
        HomeCoreProgress = HasHomeNowProgress ? HomeNowProgress / 100 : 0;
        switch (_homeNowKind)
        {
            case "mission":
                HomeCoreLabel = "GUARDIAN · IN FOCUS";
                HomeCoreBig = MissionRemaining.StartsWith("00:", StringComparison.Ordinal) ? MissionRemaining[3..] : MissionRemaining;
                HomeCoreSub = "LEFT · " + HomeNowTitle.ToUpperInvariant();
                HomeCoreAccent = CoreCyan;
                break;
            case "block":
            case "meeting":
                var current = events.FirstOrDefault(e => e.Start <= now && e.End > now && (_homeNowKind == "meeting" ? e.Attendees.Count > 0 : e.IsNekomataManaged));
                var left = current is null ? 0 : Math.Max(0, (int)Math.Ceiling((current.End - now).TotalMinutes));
                var total = current is null ? 0 : (int)(current.End - current.Start).TotalMinutes;
                HomeCoreLabel = _homeNowKind == "meeting" ? "GUARDIAN · MEETING" : "GUARDIAN · FOCUS BLOCK";
                HomeCoreBig = $"{left / 60}:{left % 60:00}";
                HomeCoreSub = $"LEFT OF {total} MIN · {(HomeOnPlan ? "ON PLAN" : "OFF PLAN")}";
                HomeCoreAccent = _homeNowKind == "meeting" ? CoreCyan : CorePurple;
                break;
            case "evening":
                var first = _homeTomorrow.Where(e => !e.IsAllDay).OrderBy(e => e.Start).FirstOrDefault();
                HomeCoreLabel = first is null ? "TOMORROW" : $"{first.Start.LocalDateTime:dddd} · FIRST UP".ToUpperInvariant();
                HomeCoreBig = first is null ? "--:--" : first.Start.LocalDateTime.ToString("HH:mm");
                HomeCoreSub = first is null ? "NOTHING BOOKED YET" : CleanCalendarObjectiveTitle(first.Subject).ToUpperInvariant();
                HomeCoreAccent = CorePurple;
                HomeCoreProgress = 0;
                break;
            default:
                var freeUntil = events.Where(e => e.Start > now).Select(e => e.Start.LocalDateTime).DefaultIfEmpty(settings.GetEnd(now.Date)).Min();
                HomeCoreLabel = "GUARDIAN · FREE TIME";
                HomeCoreBig = freeUntil.ToString("HH:mm");
                HomeCoreSub = $"FREE UNTIL · {(HomeOnPlan ? "ON PLAN" : "OFF PLAN")}";
                HomeCoreAccent = CorePurple;
                HomeCoreProgress = 0;
                break;
        }

        // The ring: today, or tomorrow in the evening.
        HomeCoreItems.Clear();
        if (_homeNowKind == "evening")
        {
            var tomorrow = _homeTomorrow.Where(e => !e.IsAllDay).OrderBy(e => e.Start).ToList();
            var day = tomorrow.Count > 0 ? tomorrow[0].Start.LocalDateTime.Date : DateTime.Today.AddDays(1);
            var start = settings.GetStart(day);
            var end = settings.GetEnd(day);
            if (tomorrow.Count > 0)
            {
                start = new[] { start, tomorrow.Min(e => e.Start.LocalDateTime) }.Min();
                end = new[] { end, tomorrow.Max(e => e.End.LocalDateTime) }.Max();
            }
            HomeCoreDayStart = start.AddMinutes(-start.Minute % 30);
            HomeCoreDayEnd = end;
            foreach (var e in tomorrow)
                HomeCoreItems.Add(new HomeTimelineItem(CleanCalendarObjectiveTitle(e.Subject), e.Start.LocalDateTime, e.End.LocalDateTime,
                    e.IsNekomataManaged ? "focus" : e.Attendees.Count > 0 ? "meeting" : "own", e.Id));
        }
        else
        {
            HomeCoreDayStart = HomeDayStart;
            HomeCoreDayEnd = HomeDayEnd;
            foreach (var item in HomeTimeline) HomeCoreItems.Add(item);
        }

        // Next up, and how much of the day is still unbooked.
        HomeNext.Clear();
        if (_homeNowKind != "evening")
        {
            foreach (var e in events.Where(e => e.Start > now).OrderBy(e => e.Start).Take(3))
                HomeNext.Add(new HomeNextItem(e.Start.LocalDateTime.ToString("HH:mm"), CleanCalendarObjectiveTitle(e.Subject)));
            var dayEnd = settings.GetEnd(now.Date);
            var from = now.LocalDateTime;
            if (dayEnd > from)
            {
                var busy = 0.0;
                var cursor = from;
                foreach (var e in events.Where(e => e.End.LocalDateTime > from && e.Start.LocalDateTime < dayEnd).OrderBy(e => e.Start))
                {
                    var s = e.Start.LocalDateTime < cursor ? cursor : e.Start.LocalDateTime;
                    var f = e.End.LocalDateTime > dayEnd ? dayEnd : e.End.LocalDateTime;
                    if (f > s) { busy += (f - s).TotalMinutes; cursor = f; }
                }
                var free = Math.Max(0, (dayEnd - from).TotalMinutes - busy);
                HomeNext.Add(new HomeNextItem($"{(int)free / 60} h {(int)free % 60:00}", "Unbooked time left today"));
            }
        }
    }

    private void RefreshHomeSignals()
    {
        HomeHeldCount = _heldSpeech.Count;
        var total = HomeServices.Count;
        var healthy = HomeServices.Count(s => ReferenceEquals(s.Dot, OkBrush));
        HomeSystemsHealthy = total == 0 ? "–" : $"{healthy}/{total}";
        var attention = HomeServices.Where(s => ReferenceEquals(s.Dot, WarningBrush)).Select(s => s.Name).ToList();
        HomeSystemsWarn = attention.Count > 0;
        HomeSystemsNote = attention.Count == 0
            ? (Diagnostics.Count == 0 ? "CHECKING" : $"CHECKED {Diagnostics.Max(d => d.CheckedAt):HH:mm}")
            : string.Join(" · ", attention).ToUpperInvariant();
    }

    /// <summary>Talk to Guardian from the home page: listen, then run what was heard.</summary>
    [RelayCommand]
    private async Task HomeTalkAsync()
    {
        CommandPaletteInput = "";
        await DictateCommandPaletteAsync();
        if (!string.IsNullOrWhiteSpace(CommandPaletteInput)) await ExecuteCommandPaletteAsync();
    }
}
