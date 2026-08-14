using System.Collections.Specialized;
using System.Windows;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;

        viewModel.ChatHistory.CollectionChanged += ChatHistory_CollectionChanged;
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