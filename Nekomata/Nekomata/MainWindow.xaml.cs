using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OpenQuickMenu();
            }
        };

        viewModel.ChatHistory.CollectionChanged += ChatHistory_CollectionChanged;
    }

    private void QuickMenu_Click(object sender, RoutedEventArgs e) => OpenQuickMenu();

    private void OpenQuickMenu()
    {
        if (DataContext is MainViewModel model)
            new Windows.CommandMenuWindow(model) { Owner = this }.ShowDialog();
    }

    private void ChatHistory_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            GuardianScrollViewer.ScrollToEnd();
        });
    }
}
