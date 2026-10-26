using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Planning;
using Nekomata.UI.Windows;

namespace Nekomata.UI.ViewModels;

// The Planner window (PLANNER on Home, or "planner"): duties, objectives and where the time goes, all editable.
public partial class MainViewModel
{
    /// <summary>What the Planner window can ask of Guardian.</summary>
    public sealed class PlannerHost
    {
        public required Func<List<Duty>> Duties { get; init; }
        public required Func<List<Objective>> Objectives { get; init; }
        public required Action SaveAll { get; init; }
        public required Action<Duty> MarkDutyDone { get; init; }
        public required Func<Task> LinkObjectives { get; init; }
        public required Func<Task> ExportObjectives { get; init; }
        public required Func<(string Today, string Week, string LastWeek, string Headline)> TimeSummaries { get; init; }
    }

    private PlannerViewModel? _plannerViewModel;
    private PlannerWindow? _plannerWindow;

    [RelayCommand]
    private void OpenPlannerWindow() => OpenPlanner("duties");

    private void OpenPlanner(string tab)
    {
        var index = tab switch { "objectives" => 1, "time" => 2, _ => 0 };
        if (_plannerWindow is { IsLoaded: true } open && _plannerViewModel is not null)
        {
            _plannerViewModel.SelectedTab = index;
            open.Activate();
            return;
        }
        var host = new PlannerHost
        {
            Duties = () => Duties,
            Objectives = () => Objectives,
            SaveAll = () => { SaveDuties(); SaveObjectives(); },
            MarkDutyDone = duty => MarkDutyDone(duty),
            LinkObjectives = () => LinkObjectivesAsync(announce: false),
            ExportObjectives = ExportObjectiveEvidenceAsync,
            TimeSummaries = PlannerTimeSummaries
        };
        _plannerViewModel = new PlannerViewModel(host) { SelectedTab = index };
        _plannerWindow = new PlannerWindow(_plannerViewModel) { Owner = Application.Current?.MainWindow };
        _plannerWindow.Closed += (_, _) =>
        {
            SaveDuties(); SaveObjectives();
            _plannerWindow = null;
            _plannerViewModel = null;
        };
        _plannerWindow.Show();
    }

    private (string, string, string, string) PlannerTimeSummaries()
    {
        var today = DateTime.Today;
        var start = WeekStart(today);
        var week = TimeLedgerDays.Where(d => d.Date >= start).ToList();
        var last = TimeLedgerDays.Where(d => d.Date >= start.AddDays(-7) && d.Date < start).ToList();
        var headline = week.Sum(TimeLedger.Total) < 1 ? "Nothing recorded this week yet."
            : $"This week {TimeLedger.ReactivePercent(week)}% reactive" + (last.Count > 0 ? $" · last week {TimeLedger.ReactivePercent(last)}%" : "") +
              $" · {TimeLedger.Hours(week.Sum(TimeLedger.Total))} recorded";
        return (TimeReport(today, today, $"TODAY · {today:dddd d MMM}"), TimeReport(start, today, $"THIS WEEK · from {start:d MMM}"),
            last.Count == 0 ? "No data for last week." : TimeReport(start.AddDays(-7), start.AddDays(-1), $"LAST WEEK · from {start.AddDays(-7):d MMM}"), headline);
    }
}
