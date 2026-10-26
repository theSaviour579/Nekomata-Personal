using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nekomata.UI.ViewModels;

/// <summary>One switch on the Guardian settings page.</summary>
public sealed partial class GuardianFeatureToggle : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    [ObservableProperty] private bool enabled = true;
}

/// <summary>One cached Microsoft account and what it can do.</summary>
public sealed class GuardianAccountRow
{
    public string Username { get; init; } = "";
    public bool IsPrimary { get; init; }
    public string Role => IsPrimary ? "EVERYDAY ACCOUNT" : "EXTRA ACCOUNT";
    public string Permissions { get; init; } = "";
}

/// <summary>Everything Guardian lets the user tune, in one page. Saving hands the values back to MainViewModel.</summary>
public sealed partial class GuardianSettingsViewModel : ObservableObject
{
    private readonly Func<GuardianSettingsViewModel, Task> _save;
    private readonly Func<Task<IReadOnlyList<GuardianAccountRow>>> _loadAccounts;
    private readonly Func<string, string, Task<string>> _accountAction;

    public GuardianSettingsViewModel(Func<GuardianSettingsViewModel, Task> save, Func<Task<IReadOnlyList<GuardianAccountRow>>> loadAccounts,
        Func<string, string, Task<string>> accountAction)
    {
        _save = save;
        _loadAccounts = loadAccounts;
        _accountAction = accountAction;
    }

    // Hours.
    [ObservableProperty] private string workStart = "08:00";
    [ObservableProperty] private string workEnd = "16:30";
    [ObservableProperty] private bool offHoursEnabled = true;

    // Teams.
    [ObservableProperty] private string managers = "";
    [ObservableProperty] private string notManagers = "";
    [ObservableProperty] private string lineManager = "";
    [ObservableProperty] private int mutedChatCount;
    public bool ClearMutedChats { get; private set; }

    // Thresholds.
    [ObservableProperty] private int projectQuietDays = 9;
    [ObservableProperty] private int meetingBudgetHours = 12;

    // Weather.
    [ObservableProperty] private string weatherLocation = "";

    public ObservableCollection<GuardianFeatureToggle> Features { get; } = [];
    public ObservableCollection<GuardianAccountRow> Accounts { get; } = [];
    [ObservableProperty] private string accountsStatus = "Checking accounts…";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool busy;

    public event Action? CloseRequested;

    public async Task LoadAccountsAsync()
    {
        AccountsStatus = "Checking what each account can do…";
        try
        {
            var rows = await _loadAccounts();
            Accounts.Clear();
            foreach (var row in rows) Accounts.Add(row);
            AccountsStatus = rows.Count == 0 ? "No Microsoft accounts signed in." : "";
        }
        catch (Exception ex) { AccountsStatus = $"Couldn't read the accounts: {ex.GetBaseException().Message}"; }
    }

    [RelayCommand]
    private void ClearMuted()
    {
        ClearMutedChats = true;
        MutedChatCount = 0;
        Status = "Muted chats will be cleared when you save.";
    }

    [RelayCommand]
    private async Task AccountAsync(string action) => await RunAccountActionAsync(action, "");

    [RelayCommand]
    private async Task MakePrimaryAsync(GuardianAccountRow? row) { if (row is not null) await RunAccountActionAsync("primary", row.Username); }

    [RelayCommand]
    private async Task SignOutAsync(GuardianAccountRow? row) { if (row is not null) await RunAccountActionAsync("signout", row.Username); }

    private async Task RunAccountActionAsync(string action, string username)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            Status = await _accountAction(action, username);
            await LoadAccountsAsync();
        }
        catch (Exception ex) { Status = ex.GetBaseException().Message; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TimeSpan.TryParse(WorkStart, out var start) || !TimeSpan.TryParse(WorkEnd, out var end) || end <= start)
        {
            Status = "Working hours need to be times like 08:00 and 16:30, with the end after the start.";
            return;
        }
        Busy = true;
        try
        {
            await _save(this);
            Status = $"Saved {DateTime.Now:HH:mm}.";
            ClearMutedChats = false;
        }
        catch (Exception ex) { Status = $"Not saved: {ex.GetBaseException().Message}"; }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
