using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class QuickWorkLogWindow : Window
{
    public QuickWorkLogWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext=viewModel;
    }
}
