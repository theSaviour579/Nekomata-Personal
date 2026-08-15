using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Services;
using System.Reflection;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string updateStatus = "Check for a newer private release.";
    [ObservableProperty] private bool updateAvailable;
    [ObservableProperty] private bool updateCheckBusy;
    [ObservableProperty] private bool startWithWindows;
    private UpdateCheckResult? _latestUpdate;

    public string InstalledVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development";

    private void InitialiseReleaseSettings()
    {
        StartWithWindows = _services.GetRequiredService<StartupRegistrationService>().IsEnabled;
        _ = CheckForUpdatesAsync();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        try
        {
            _services.GetRequiredService<StartupRegistrationService>().SetEnabled(value);
            if (_personalProfile.IsConfigured)
                _personalProfile.Save(_personalProfile.Current.DisplayName, value);
        }
        catch (Exception ex) { UpdateStatus = "Startup preference could not be saved: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (UpdateCheckBusy) return;
        UpdateCheckBusy = true;
        try
        {
            var result = await _services.GetRequiredService<UpdateCheckService>().CheckAsync();
            UpdateStatus = result.Status;
            UpdateAvailable = result.UpdateAvailable;
            _latestUpdate = result;
        }
        catch (Exception ex) { UpdateStatus = "Update check failed: " + ex.Message; }
        finally { UpdateCheckBusy = false; }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_latestUpdate is not { UpdateAvailable: true } update || UpdateCheckBusy) return;
        var confirm = MessageBox.Show(
            $"Download and install Nekomata Personal {update.LatestVersion}?\n\nThe installer is verified before it runs. Nekomata will close during installation and Windows will reopen it afterwards.",
            "Install Nekomata Update", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (confirm != MessageBoxResult.Yes) return;

        UpdateCheckBusy = true;
        try
        {
            var progress = new Progress<string>(status => UpdateStatus = status);
            var service = _services.GetRequiredService<UpdateCheckService>();
            var result = await service.DownloadInstallerAsync(update, progress);
            UpdateStatus = result.Message;
            if (!result.Success || result.InstallerPath is null)
            {
                MessageBox.Show(result.Message, "Update could not be installed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatus = "Starting the verified installer…";
            UpdateCheckService.LaunchInstaller(result.InstallerPath);
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update installation failed: " + ex.GetBaseException().Message;
            MessageBox.Show(UpdateStatus, "Update could not be installed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { UpdateCheckBusy = false; }
    }
}
