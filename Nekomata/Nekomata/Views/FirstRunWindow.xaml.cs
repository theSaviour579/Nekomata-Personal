using Nekomata.UI.Services;
using Nekomata.UI.ViewModels;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class FirstRunWindow : Window
{
    private readonly FirstRunService _firstRun;
    private readonly StartupRegistrationService _startup;
    private readonly MainViewModel _main;

    public FirstRunWindow(FirstRunService firstRun, StartupRegistrationService startup, MainViewModel main)
    {
        _firstRun = firstRun;
        _startup = startup;
        _main = main;
        InitializeComponent();
        StartupChoice.IsChecked = startup.IsEnabled;
    }

    private void OpenSetup_Click(object sender, RoutedEventArgs e)
    {
        Complete();
        _main.ShowDiagnosticsCommand.Execute(null);
        DialogResult = true;
    }

    private void FinishLater_Click(object sender, RoutedEventArgs e)
    {
        Complete();
        DialogResult = false;
    }

    private void Complete()
    {
        _startup.SetEnabled(StartupChoice.IsChecked == true);
        _main.StartWithWindows = StartupChoice.IsChecked == true;
        _firstRun.Complete();
    }
}
