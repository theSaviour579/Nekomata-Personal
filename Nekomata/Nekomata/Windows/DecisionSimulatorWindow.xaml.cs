using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public partial class DecisionSimulatorWindow : Window
{
    public DecisionSimulatorWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && await viewModel.PrepareDecisionScenarioForReviewAsync())
            Close();
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { SelectedDecisionScenario: { CanPrepare: true } scenario } viewModel)
            return;

        var answer = MessageBox.Show(this,
            $"Create {scenario.Blocks.Count} focus block(s) in your Microsoft 365 calendar?\n\n" +
            "Guardian will re-check the live calendar first. The plan is applied atomically and the batch can be undone from Calendar.",
            "Approve calendar sync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (await viewModel.SyncDecisionScenarioToCalendarAsync())
            Close();
    }
}
