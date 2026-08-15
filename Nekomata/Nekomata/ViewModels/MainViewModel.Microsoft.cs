using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Integrations.MicrosoftGraph.Authentication;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty]
    private string microsoftAccountStatus = "Not connected";

    [ObservableProperty]
    private bool microsoftAccountBusy;

    [ObservableProperty]
    private bool microsoftAccountAvailable;

    [ObservableProperty]
    private bool microsoftAccountConnected;

    public string MicrosoftConnectLabel => MicrosoftAccountConnected ? "CHANGE ACCOUNT" : "CONNECT MICROSOFT";

    [RelayCommand(CanExecute = nameof(CanConnectMicrosoftAccount))]
    private async Task ConnectMicrosoftAccountAsync()
    {
        if (MicrosoftAccountBusy) return;
        MicrosoftAccountBusy = true;
        MicrosoftAccountStatus = "Opening Microsoft sign-in…";
        try
        {
            var authentication = _services.GetRequiredService<IMicrosoftAuthenticationService>();
            if (MicrosoftAccountConnected)
            {
                await authentication.DisconnectAsync();
                MicrosoftAccountConnected = false;
            }
            var token = await authentication.GetTokenAsync();
            MicrosoftAccountStatus = string.IsNullOrWhiteSpace(token.AccountName)
                ? "Connected"
                : $"Connected as {token.AccountName}";
            MicrosoftAccountConnected = true;
            await RefreshDailyBriefingContextAsync();
        }
        catch (Exception ex)
        {
            MicrosoftAccountStatus = "Could not connect: " + ex.GetBaseException().Message;
        }
        finally
        {
            MicrosoftAccountBusy = false;
        }
    }

    private bool CanConnectMicrosoftAccount() => MicrosoftAccountAvailable && !MicrosoftAccountBusy;

    [RelayCommand(CanExecute = nameof(CanDisconnectMicrosoftAccount))]
    private async Task DisconnectMicrosoftAccountAsync()
    {
        if (MicrosoftAccountBusy) return;
        MicrosoftAccountBusy = true;
        try
        {
            await _services.GetRequiredService<IMicrosoftAuthenticationService>().DisconnectAsync();
            MicrosoftAccountConnected = false;
            MicrosoftAccountStatus = "Not connected";
        }
        catch (Exception ex) { MicrosoftAccountStatus = "Could not disconnect: " + ex.GetBaseException().Message; }
        finally { MicrosoftAccountBusy = false; }
    }

    private bool CanDisconnectMicrosoftAccount() => MicrosoftAccountAvailable && MicrosoftAccountConnected && !MicrosoftAccountBusy;

    private void InitialiseMicrosoftAccount()
    {
        var options = _services.GetRequiredService<MicrosoftGraphOptions>();
        MicrosoftAccountAvailable = !string.IsNullOrWhiteSpace(options.ClientId);
        MicrosoftAccountStatus = MicrosoftAccountAvailable
            ? "Not connected"
            : "Microsoft sign-in will be enabled when the application registration is connected.";
        if (MicrosoftAccountAvailable) _ = RefreshMicrosoftAccountStatusAsync();
    }

    private async Task RefreshMicrosoftAccountStatusAsync()
    {
        try
        {
            var account = await _services.GetRequiredService<IMicrosoftAuthenticationService>().GetConnectedAccountAsync();
            MicrosoftAccountConnected = !string.IsNullOrWhiteSpace(account);
            MicrosoftAccountStatus = MicrosoftAccountConnected ? $"Connected as {account}" : "Not connected";
        }
        catch { MicrosoftAccountConnected = false; }
    }

    partial void OnMicrosoftAccountBusyChanged(bool value)
    {
        ConnectMicrosoftAccountCommand.NotifyCanExecuteChanged();
        DisconnectMicrosoftAccountCommand.NotifyCanExecuteChanged();
    }
    partial void OnMicrosoftAccountAvailableChanged(bool value)
    {
        ConnectMicrosoftAccountCommand.NotifyCanExecuteChanged();
        DisconnectMicrosoftAccountCommand.NotifyCanExecuteChanged();
    }
    partial void OnMicrosoftAccountConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(MicrosoftConnectLabel));
        DisconnectMicrosoftAccountCommand.NotifyCanExecuteChanged();
    }
}
