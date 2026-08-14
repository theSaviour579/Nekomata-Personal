using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void OpenGuardianActivity()
    {
        var window = _services.GetRequiredService<GuardianActivityWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }
}
