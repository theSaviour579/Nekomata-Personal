using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Nekomata.Data.Local;
using Nekomata.UI.Services;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private string backupStatus = "Create an encrypted copy to protect or move your workspace.";
    [ObservableProperty] private bool backupBusy;
    public string PersonalDisplayName => _personalProfile.Current.DisplayName;
    public string OpenAiKeyStatus => _services.GetRequiredService<PersonalSecretService>().HasOpenAiApiKey
        ? "OpenAI key saved securely"
        : "OpenAI key not configured";

    [RelayCommand]
    private void EditPersonalSettings()
    {
        var window = new PersonalSettingsWindow(_personalProfile, _services.GetRequiredService<PersonalSecretService>())
        {
            Owner = Application.Current.MainWindow
        };
        if (window.ShowDialog() != true) return;
        ApplyPersonalProfile();
        _ = NormalizeLegacyPersonalNamesAsync();
        OnPropertyChanged(nameof(PersonalDisplayName));
        OnPropertyChanged(nameof(OpenAiKeyStatus));
    }

    private async Task NormalizeLegacyPersonalNamesAsync()
    {
        var displayName = _personalProfile.Current.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return;

        static bool IsLegacyOwner(string? owner) =>
            string.IsNullOrWhiteSpace(owner);

        foreach (var task in Workspace.Tasks.Where(task => IsLegacyOwner(task.Owner)))
            task.Owner = displayName;

        await _services.GetRequiredService<LocalWorkspaceStore>().UpdateAsync(data =>
        {
            foreach (var task in data.Tasks.Where(task => IsLegacyOwner(task.Owner)))
                task.Owner = displayName;
            return true;
        });
        OnPropertyChanged(nameof(Workspace));
    }

    private bool EnsurePersonalAiConfigured()
    {
        if (_services.GetRequiredService<PersonalAIProvider>().IsConfigured) return true;

        var choice = MessageBox.Show(
            "Guardian needs an OpenAI API key to answer this request. Would you like to add one now?\n\nThe key will be stored securely in Windows Credential Manager.",
            "Enable Guardian AI", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Yes) EditPersonalSettings();

        if (_services.GetRequiredService<PersonalAIProvider>().IsConfigured) return true;
        GuardianResponse = "Guardian needs an OpenAI API key before it can answer this request. You can add one from Settings → Your Assistant.";
        return false;
    }

    [RelayCommand]
    private async Task CreatePersonalBackupAsync()
    {
        var passphrase = RequestBackupPassword();
        if (passphrase is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "Save encrypted Nekomata Personal backup",
            Filter = "Nekomata Personal backup (*.nkp)|*.nkp",
            DefaultExt = ".nkp",
            AddExtension = true,
            FileName = $"nekomata-personal-{DateTime.Now:yyyyMMdd-HHmmss}.nkp"
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        BackupBusy = true;
        try
        {
            var result = await _services.GetRequiredService<PersonalBackupService>().CreateAsync(dialog.FileName, passphrase);
            BackupStatus = result.Message;
            MessageBox.Show(result.Message, "Nekomata Personal Backup", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally { BackupBusy = false; }
    }

    [RelayCommand]
    private async Task RestorePersonalBackupAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a Nekomata Personal backup",
            Filter = "Nekomata Personal backup (*.nkp)|*.nkp",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true) return;
        var passphrase = RequestBackupPassword();
        if (passphrase is null) return;
        var confirm = MessageBox.Show(
            "Restore will replace the tasks, projects, planning history and profile currently stored on this computer.\n\nOpenAI and Microsoft credentials are not changed. Continue?",
            "Restore Personal Workspace", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        BackupBusy = true;
        try
        {
            var result = await _services.GetRequiredService<PersonalBackupService>().RestoreAsync(dialog.FileName, passphrase);
            BackupStatus = result.Message;
            if (result.Success)
            {
                ApplyPersonalProfile();
                OnPropertyChanged(nameof(PersonalDisplayName));
                Workspace = await _workspaceCoordinator.RefreshAsync();
            }
            MessageBox.Show(result.Message, "Nekomata Personal Restore", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally { BackupBusy = false; }
    }

    private static string? RequestBackupPassword()
    {
        var window = new BackupPasswordWindow { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Passphrase : null;
    }
}
