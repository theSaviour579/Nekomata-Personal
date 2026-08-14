using Nekomata.UI.ViewModels;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class GuardianActivityWindow : Window
{
    public GuardianActivityWindow(GuardianActivityViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
