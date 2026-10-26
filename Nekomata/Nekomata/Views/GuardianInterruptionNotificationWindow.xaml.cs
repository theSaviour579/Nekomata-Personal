using Nekomata.UI.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace Nekomata.UI.Views;

public partial class GuardianInterruptionNotificationWindow : Window
{
    private readonly MainViewModel _viewModel;

    public GuardianInterruptionNotificationWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelPropertyChanged;
        Closed += (_, _) => viewModel.PropertyChanged -= ViewModelPropertyChanged;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, area.Right - ActualWidth - 20);
            Top = area.Top + 20;
        };
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.InterruptionPanelVisible) && !_viewModel.InterruptionPanelVisible)
            Close();
    }
}
