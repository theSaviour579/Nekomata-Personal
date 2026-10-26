using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Planning;

namespace Nekomata.UI.ViewModels;

public sealed partial class DutyRow : ObservableObject
{
    public Duty Model { get; }
    public DutyRow(Duty model) { Model = model; }
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); Refresh(); } }
    public string Frequency { get => Model.Frequency; set { Model.Frequency = (value ?? "monthly").Trim().ToLowerInvariant(); OnPropertyChanged(); Refresh(); } }
    public int Day { get => Model.Day; set { Model.Day = value; OnPropertyChanged(); Refresh(); } }
    public int Month { get => Model.Month; set { Model.Month = value; OnPropertyChanged(); Refresh(); } }
    public int EveryDays { get => Model.EveryDays; set { Model.EveryDays = value; OnPropertyChanged(); Refresh(); } }
    public int LeadDays { get => Model.LeadDays; set { Model.LeadDays = value; OnPropertyChanged(); } }
    public string Evidence { get => Model.Evidence; set { Model.Evidence = value ?? ""; OnPropertyChanged(); } }
    public string Notes { get => Model.Notes; set { Model.Notes = value ?? ""; OnPropertyChanged(); } }
    public bool Enabled { get => Model.Enabled; set { Model.Enabled = value; OnPropertyChanged(); } }
    public string LastDoneText => Model.LastDone is { } d ? d.ToString("d MMM yyyy") : "never";
    public string NextDueText => DutySchedule.NextDue(Model, DateTime.Today) is var due && due < DateTime.Today ? $"OVERDUE {due:d MMM}" : DutySchedule.NextDue(Model, DateTime.Today).ToString("ddd d MMM yyyy");
    public string Schedule => DutySchedule.Describe(Model);
    public void Refresh() { OnPropertyChanged(nameof(NextDueText)); OnPropertyChanged(nameof(Schedule)); OnPropertyChanged(nameof(LastDoneText)); }
}

public sealed partial class ObjectiveRow : ObservableObject
{
    public Objective Model { get; }
    public ObjectiveRow(Objective model) { Model = model; }
    public string Title { get => Model.Title; set { Model.Title = value ?? ""; OnPropertyChanged(); } }
    public string Measure { get => Model.Measure; set { Model.Measure = value ?? ""; OnPropertyChanged(); } }
    public string DueText
    {
        get => Model.Due?.ToString("d MMM yyyy") ?? "";
        set { Model.Due = DateTime.TryParse(value, out var d) ? d.Date : null; OnPropertyChanged(); }
    }
    public int Progress { get => Model.Progress; set { Model.Progress = Math.Clamp(value, 0, 100); OnPropertyChanged(); } }
    public bool Active { get => Model.Active; set { Model.Active = value; OnPropertyChanged(); } }
    public string EvidenceText => Model.Evidence.Count == 0 ? "none yet" :
        $"{Model.Evidence.Count} · latest {Model.Evidence.Max(e => e.At):d MMM}: {Model.Evidence.OrderByDescending(e => e.At).First().Text}";
    public void Refresh() => OnPropertyChanged(nameof(EvidenceText));
}

/// <summary>The Planner window: duties, objectives and where the time goes.</summary>
public sealed partial class PlannerViewModel : ObservableObject
{
    private readonly MainViewModel.PlannerHost _host;

    public PlannerViewModel(MainViewModel.PlannerHost host)
    {
        _host = host;
        RefreshDuties();
        RefreshObjectives();
        RefreshTime();
    }

    public ObservableCollection<DutyRow> Duties { get; } = [];
    public ObservableCollection<ObjectiveRow> Objectives { get; } = [];
    public IReadOnlyList<string> Frequencies { get; } = ["weekly", "monthly", "quarterly", "yearly", "days"];

    [ObservableProperty] private DutyRow? selectedDuty;
    [ObservableProperty] private ObjectiveRow? selectedObjective;
    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string timeToday = "";
    [ObservableProperty] private string timeWeek = "";
    [ObservableProperty] private string timeLastWeek = "";
    [ObservableProperty] private string timeHeadline = "";

    public void RefreshDuties()
    {
        var keep = SelectedDuty?.Model;
        Duties.Clear();
        foreach (var d in _host.Duties().OrderBy(d => DutySchedule.NextDue(d, DateTime.Today))) Duties.Add(new DutyRow(d));
        SelectedDuty = Duties.FirstOrDefault(r => ReferenceEquals(r.Model, keep));
    }

    public void RefreshObjectives()
    {
        var keep = SelectedObjective?.Model;
        Objectives.Clear();
        foreach (var o in _host.Objectives()) Objectives.Add(new ObjectiveRow(o));
        SelectedObjective = Objectives.FirstOrDefault(r => ReferenceEquals(r.Model, keep));
    }

    public void RefreshTime()
    {
        var (today, week, lastWeek, headline) = _host.TimeSummaries();
        TimeToday = today; TimeWeek = week; TimeLastWeek = lastWeek; TimeHeadline = headline;
    }

    [RelayCommand]
    private void SaveAll()
    {
        _host.SaveAll();
        foreach (var d in Duties) d.Refresh();
        Status = $"Saved {DateTime.Now:HH:mm}.";
    }

    [RelayCommand]
    private void AddDuty()
    {
        var duty = new Duty { Name = "New duty", Frequency = "monthly", Day = 1 };
        _host.Duties().Add(duty);
        var row = new DutyRow(duty);
        Duties.Insert(0, row);
        SelectedDuty = row;
        Status = "New duty added at the top. Edit it, then Save.";
    }

    [RelayCommand]
    private void DutyDone(DutyRow? row)
    {
        row ??= SelectedDuty;
        if (row is null) return;
        _host.MarkDutyDone(row.Model);
        row.Refresh();
        Status = $"Ticked off: {row.Name}. Next due {row.NextDueText}.";
    }

    [RelayCommand]
    private void RemoveDuty()
    {
        if (SelectedDuty is not { } row) return;
        _host.Duties().Remove(row.Model);
        Duties.Remove(row);
        _host.SaveAll();
        Status = $"Removed {row.Name}.";
    }

    [RelayCommand]
    private void AddObjective()
    {
        var objective = new Objective { Title = "New objective" };
        _host.Objectives().Add(objective);
        var row = new ObjectiveRow(objective);
        Objectives.Insert(0, row);
        SelectedObjective = row;
        Status = "New objective added. Give it a title, measure and due date, then Save.";
    }

    [RelayCommand]
    private void RemoveObjective()
    {
        if (SelectedObjective is not { } row) return;
        _host.Objectives().Remove(row.Model);
        Objectives.Remove(row);
        _host.SaveAll();
        Status = $"Removed {row.Title}.";
    }

    [RelayCommand]
    private async Task LinkObjectivesAsync()
    {
        _host.SaveAll();
        Status = "Linking recent work to your objectives…";
        await _host.LinkObjectives();
        RefreshObjectives();
        Status = $"Linked at {DateTime.Now:HH:mm}.";
    }

    [RelayCommand]
    private async Task ExportObjectivesAsync()
    {
        _host.SaveAll();
        await _host.ExportObjectives();
        Status = "Opened in Word.";
    }
}
