using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class DecisionSimulatorWindow : Window
{
    public DecisionSimulatorWindow(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && await viewModel.PrepareDecisionScenarioForReviewAsync()) Close();
    }
}
