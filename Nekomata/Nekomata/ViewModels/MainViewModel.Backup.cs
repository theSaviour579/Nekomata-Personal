using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Nekomata.UI.Services;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string backupStatus = "Checking backup protection…";
    [ObservableProperty] private bool backupBusy;

    private async Task InitialiseAutomaticBackupAsync()
    {
        try
        {
            var service = _services.GetRequiredService<DatabaseBackupService>();
            var result = await service.EnsureDailyBackupAsync();
            BackupStatus = result.Message;
        }
        catch (Exception ex) { BackupStatus = "Automatic backup check failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CreateEncryptedBackupAsync()
    {
        var passphrase = RequestBackupPassword();
        if (passphrase is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save encrypted Nekomata backup",
            Filter = "Nekomata encrypted backup (*.nkb)|*.nkb",
            DefaultExt = ".nkb",
            AddExtension = true,
            FileName = $"nekomata-{DateTime.Now:yyyyMMdd-HHmmss}.nkb"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        BackupBusy = true;
        try
        {
            var result = await _services.GetRequiredService<DatabaseBackupService>().CreateBackupAsync(dialog.FileName, passphrase);
            BackupStatus = result.Message;
            MessageBox.Show(result.Message, "Nekomata Backup", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await RefreshDiagnosticsAsync();
        }
        finally { BackupBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreEncryptedBackupAsync()
    {
        var dialog = new OpenFileDialog { Title = "Select a Nekomata backup", Filter = "Nekomata encrypted backup (*.nkb)|*.nkb", CheckFileExists = true };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        var passphrase = RequestBackupPassword();
        if (passphrase is null) return;
        var confirm = MessageBox.Show(
            "Restore will replace the current Nekomata workspace database with this backup.\n\nAny newer tasks, projects, Guardian history and mission data will be lost. Continue?",
            "Confirm Database Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        BackupBusy = true;
        try
        {
            var result = await _services.GetRequiredService<DatabaseBackupService>().RestoreAsync(dialog.FileName, passphrase);
            BackupStatus = result.Message;
            MessageBox.Show(result.Message, "Nekomata Restore", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally { BackupBusy = false; }
    }

    private static string? RequestBackupPassword()
    {
        var window = new BackupPasswordWindow { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Passphrase : null;
    }
}
