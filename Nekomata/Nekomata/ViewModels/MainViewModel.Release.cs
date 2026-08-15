using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Services;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string updateStatus = "Check for a newer private release.";
    [ObservableProperty] private bool updateAvailable;
    [ObservableProperty] private bool updateCheckBusy;
    [ObservableProperty] private bool startWithWindows;
    private string? _latestReleaseUrl;

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
            _latestReleaseUrl = result.ReleaseUrl;
        }
        catch (Exception ex) { UpdateStatus = "Update check failed: " + ex.Message; }
        finally { UpdateCheckBusy = false; }
    }

    [RelayCommand]
    private void OpenLatestRelease()
    {
        if (string.IsNullOrWhiteSpace(_latestReleaseUrl)) return;
        Process.Start(new ProcessStartInfo(_latestReleaseUrl) { UseShellExecute = true });
    }
}
