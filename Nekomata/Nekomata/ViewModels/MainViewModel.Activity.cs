using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void OpenGuardianActivity()
        => OpenGuardianActivityAt(0);

    private void OpenProductFriction()
        => OpenGuardianActivityAt(4);

    private void OpenGuardianActivityAt(int tabIndex)
    {
        var window = _services.GetRequiredService<GuardianActivityWindow>();
        if (window.DataContext is GuardianActivityViewModel viewModel) viewModel.SelectedTabIndex = tabIndex;
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
}
