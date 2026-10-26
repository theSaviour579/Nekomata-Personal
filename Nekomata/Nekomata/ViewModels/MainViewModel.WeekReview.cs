using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Integrations.MicrosoftGraph.Models;

namespace Nekomata.UI.ViewModels;

/// <summary>One day's numbers, kept so the week can be reviewed against the last.</summary>
public sealed class HomeDaySnapshot
{
    public double FocusMinutes { get; set; }
    public double MeetingMinutes { get; set; }
    public int Done { get; set; }
    public int TasksOpen { get; set; }
    public int TasksOverduePeak { get; set; }
    public int YouOwe { get; set; }
    public int Waiting { get; set; }
}

// The week in review, and the calm switch. Every few minutes the home page notes today's numbers (focus booked,
// meetings, things Guardian did, open and overdue tasks, promises) in %LOCALAPPDATA%\Nekomata Personal\home-days.json. On a
// Friday afternoon (or whenever asked) it sets this week against last week, shows it on the home page and, once on
// Friday, reads it out.
public partial class MainViewModel
{
    private static string HomeDaysPath => Path.Combine(DataFolder, "home-days.json");
    private static string HomeUiPath => Path.Combine(DataFolder, "home-ui.json");

    private Dictionary<string, HomeDaySnapshot>? _homeDays;
    private DateTimeOffset _homeDaysSaved = DateTimeOffset.MinValue;
    private DateTime _reviewSpokenOn = DateTime.MinValue;

    [ObservableProperty] private bool homeShowReview;
    [ObservableProperty] private string homeReviewTitle = "THE WEEK IN REVIEW";
    [ObservableProperty] private bool homeCalm;
    [ObservableProperty] private int homeIntroCount;
    public ObservableCollection<string> HomeReview { get; } = [];

    private sealed class HomeUiSettings { public bool Calm { get; set; } }

    private void LoadHomeUi()
    {
        try
        {
            if (File.Exists(HomeUiPath)) HomeCalm = JsonSerializer.Deserialize<HomeUiSettings>(File.ReadAllText(HomeUiPath))?.Calm == true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Home UI settings unreadable: {ex.Message}"); }
    }

    partial void OnHomeCalmChanged(bool value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HomeUiPath)!);
            File.WriteAllText(HomeUiPath, JsonSerializer.Serialize(new HomeUiSettings { Calm = value }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Home UI settings not saved: {ex.Message}"); }
    }

    [RelayCommand] private void ToggleHomeCalm() => HomeCalm = !HomeCalm;

    [RelayCommand]
    private void ToggleHomeReview()
    {
        HomeShowReview = !HomeShowReview;
        if (HomeShowReview) BuildWeekReview(DateTimeOffset.Now);
    }

    private Dictionary<string, HomeDaySnapshot> HomeDays()
    {
        if (_homeDays is not null) return _homeDays;
        try
        {
            _homeDays = File.Exists(HomeDaysPath)
                ? JsonSerializer.Deserialize<Dictionary<string, HomeDaySnapshot>>(File.ReadAllText(HomeDaysPath)) ?? []
                : [];
        }
        catch { _homeDays = []; }
        return _homeDays;
    }

    private void RecordHomeDay(List<CalendarEvent> events, DateTimeOffset now)
    {
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return;
        var days = HomeDays();
        var key = now.Date.ToString("yyyy-MM-dd");
        if (!days.TryGetValue(key, out var day)) days[key] = day = new HomeDaySnapshot();
        day.FocusMinutes = events.Where(e => e.IsNekomataManaged).Sum(e => (e.End - e.Start).TotalMinutes);
        day.MeetingMinutes = events.Where(e => !e.IsNekomataManaged && e.Attendees.Count > 0).Sum(e => (e.End - e.Start).TotalMinutes);
        day.Done = Math.Max(day.Done, HomeDoneTotal);
        var openTasks = Workspace.Tasks.Where(t => !t.Completed).ToList();
        day.TasksOpen = openTasks.Count;
        day.TasksOverduePeak = Math.Max(day.TasksOverduePeak, openTasks.Count(t => t.DueAt is { } due && due < now.LocalDateTime));
        day.YouOwe = YouOweCount;
        day.Waiting = WaitingOnOthersCount;

        if (now - _homeDaysSaved > TimeSpan.FromMinutes(5))
        {
            _homeDaysSaved = now;
            foreach (var old in days.Keys.Where(k => DateTime.TryParse(k, out var d) && d < DateTime.Today.AddDays(-70)).ToList()) days.Remove(old);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HomeDaysPath)!);
                File.WriteAllText(HomeDaysPath, JsonSerializer.Serialize(days));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Home day log not saved: {ex.Message}"); }
        }

        // Friday afternoon: show the review, and read it out once.
        var friday = now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 12;
        if (friday && !HomeShowReview) { HomeShowReview = true; BuildWeekReview(now); }
        else if (HomeShowReview) BuildWeekReview(now);
        if (now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 15 && _reviewSpokenOn != now.Date && HomeReview.Count > 0)
        {
            _reviewSpokenOn = now.Date;
            _ = SpeakGuardianAsync("Here's your week. " + string.Join(" ", HomeReview.Take(4)));
        }
    }

    private void BuildWeekReview(DateTimeOffset now)
    {
        var days = HomeDays();
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        List<HomeDaySnapshot> Week(DateTime start) => Enumerable.Range(0, 5)
            .Select(i => start.AddDays(i)).Where(d => d <= now.Date)
            .Select(d => days.GetValueOrDefault(d.ToString("yyyy-MM-dd"))).OfType<HomeDaySnapshot>().ToList();
        var thisWeek = Week(monday);
        var lastWeek = Enumerable.Range(0, 5).Select(i => monday.AddDays(i - 7))
            .Select(d => days.GetValueOrDefault(d.ToString("yyyy-MM-dd"))).OfType<HomeDaySnapshot>().ToList();

        HomeReview.Clear();
        HomeReviewTitle = $"THE WEEK IN REVIEW · {monday:d MMM} – {now:d MMM}";
        if (thisWeek.Count == 0)
        {
            HomeReview.Add("Guardian started keeping a daily record today, so the first full review will be next Friday.");
            return;
        }
        static string H(double minutes) => minutes < 1 ? "none" : minutes < 60 ? $"{(int)minutes} min" : $"{(int)minutes / 60} h {(int)minutes % 60:00}";
        var weekdaysSoFar = Enumerable.Range(0, 5).Select(i => monday.AddDays(i)).Count(d => d <= now.Date);
        if (thisWeek.Count < weekdaysSoFar)
            HomeReview.Add(thisWeek.Count == 1
                ? "Guardian only started keeping a daily record today, so this is a one-day picture for now."
                : $"Guardian has {thisWeek.Count} of {weekdaysSoFar} days on record this week.");
        string Versus(double current, double last, Func<double, string> show) => lastWeek.Count == 0 ? "" : $" (last week {show(last)})";

        var focus = thisWeek.Sum(d => d.FocusMinutes);
        HomeReview.Add($"Focus booked: {H(focus)} across {thisWeek.Count} day{(thisWeek.Count == 1 ? "" : "s")}" + Versus(focus, lastWeek.Sum(d => d.FocusMinutes), H) + ".");
        var meetings = thisWeek.Sum(d => d.MeetingMinutes);
        HomeReview.Add($"Meetings: {H(meetings)}" + Versus(meetings, lastWeek.Sum(d => d.MeetingMinutes), H) + ".");
        var done = thisWeek.Sum(d => d.Done);
        if (done > 0 || lastWeek.Sum(d => d.Done) > 0)
            HomeReview.Add($"Guardian did {done} thing{(done == 1 ? "" : "s")} for you" + Versus(done, lastWeek.Sum(d => d.Done), v => $"{v:0}") + ".");
        var overduePeak = thisWeek.Max(d => d.TasksOverduePeak);
        HomeReview.Add(thisWeek.Count == 1
            ? $"Your tasks: {thisWeek[^1].TasksOpen} open{(overduePeak > 0 ? $", with up to {overduePeak} overdue" : ", none overdue")}."
            : $"Your tasks: {thisWeek[0].TasksOpen} open at the start of the week, {thisWeek[^1].TasksOpen} now; overdue peaked at {overduePeak}" +
              Versus(overduePeak, lastWeek.Count == 0 ? 0 : lastWeek.Max(d => d.TasksOverduePeak), v => $"{v:0}") + ".");
        var owedChange = thisWeek[^1].YouOwe - thisWeek[0].YouOwe;
        HomeReview.Add($"Promises you owe: {thisWeek[^1].YouOwe}" + (thisWeek.Count > 1 ? $" ({(owedChange <= 0 ? "down" : "up")} {Math.Abs(owedChange)} since Monday)" : "") + $"; waiting on others: {thisWeek[^1].Waiting}.");
    }
}
