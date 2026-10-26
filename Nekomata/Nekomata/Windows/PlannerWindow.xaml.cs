using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class PlannerWindow : Window
{
    public PlannerWindow(PlannerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
