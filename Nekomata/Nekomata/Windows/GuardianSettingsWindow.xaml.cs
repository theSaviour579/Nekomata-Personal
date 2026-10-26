using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class GuardianSettingsWindow : Window
{
    public GuardianSettingsWindow(GuardianSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
        Loaded += async (_, _) => await viewModel.LoadAccountsAsync();
    }
}
