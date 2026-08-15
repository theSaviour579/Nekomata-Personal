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

    [RelayCommand]
    private async Task ConnectMicrosoftAccountAsync()
    {
        if (MicrosoftAccountBusy) return;
        MicrosoftAccountBusy = true;
        MicrosoftAccountStatus = "Opening Microsoft sign-in…";
        try
        {
            var token = await _services.GetRequiredService<IMicrosoftAuthenticationService>().GetTokenAsync();
            MicrosoftAccountStatus = string.IsNullOrWhiteSpace(token.AccountName)
                ? "Connected"
                : $"Connected as {token.AccountName}";
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
}
