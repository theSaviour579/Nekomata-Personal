using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.Core.Guardian.Outcomes;
using Nekomata.UI.Windows;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string quickWorkStart = "";
    [ObservableProperty] private string quickWorkDuration = "30";

    [RelayCommand]
    private void OpenQuickWorkLog()
    {
        AdHocObjectiveTitle="";
        AdHocObjectiveOutcome="";
        AdHocObjectiveStatus="";
        QuickWorkStart=(DateTime.Now.TimeOfDay.TotalMinutes>=30 ? DateTime.Now.AddMinutes(-30) : DateTime.Today).ToString("HH:mm");
        QuickWorkDuration="30";
        new QuickWorkLogWindow(this) { Owner=Application.Current.MainWindow }.ShowDialog();
    }

    [RelayCommand]
    private async Task SaveQuickWorkLogAsync()
    {
        if(!RetrospectiveObjectiveInput.TryParseDurationToday(QuickWorkStart,QuickWorkDuration,DateTime.Now,out var period,out var error))
        { AdHocObjectiveStatus=error; return; }
        AdHocObjectiveStartedAtText=period!.StartedAt.ToString("g");
        AdHocObjectiveFinishedAtText=period.FinishedAt.ToString("g");
        try { await AddAdHocObjectiveAsync(); }
        catch(Exception exception) { AdHocObjectiveStatus=$"Could not finish saving: {exception.Message}. You can retry the same entry without adding a second work session."; }
    }
}
