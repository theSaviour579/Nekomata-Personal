using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

public sealed class CommandMenuWindow : Window
{
    private sealed record Entry(string Title, ICommand Command);
    private readonly ListBox _results = new() { DisplayMemberPath = nameof(Entry.Title), Margin = new Thickness(0, 12, 0, 0) };
    private readonly Entry[] _entries;

    public CommandMenuWindow(MainViewModel model)
    {
        Title = "Quick menu";
        Width = 460;
        Height = 450;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "NekoBackgroundBrush");
        SetResourceReference(ForegroundProperty, "NekoTextBrush");
        _entries = [
            new("Dashboard", model.ShowDashboardCommand),
            new("Pre-flight", model.ShowMorningPreflightCommand),
            new("Wrap-up", model.ShowDayReviewCommand),
            new("Log work", model.OpenQuickWorkLogCommand),
            new("Learning", model.ShowDecisionLearningCommand),
            new("Follow-ups", model.ShowPersonalFollowupsCommand),
            new("Calendar", model.ShowCalendarCommand),
            new("Email", model.ShowEmailCommand),
            new("Meeting analyzer", model.OpenGuardianPlannerCommand),
            new("Guardian activity", model.OpenGuardianActivityCommand),
            new("Attention", model.ShowAttentionCommand),
            new("Personal settings", model.EditPersonalSettingsCommand),
            new("Diagnostics", model.ShowDiagnosticsCommand)
        ];
        var panel = new DockPanel { Margin = new Thickness(20) };
        var search = new TextBox { ToolTip = "Search commands" };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search commands");
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        panel.Children.Add(_results);
        Content = panel;
        void Filter()
        {
            _results.ItemsSource = _entries.Where(entry => entry.Title.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            _results.SelectedIndex = _results.Items.Count > 0 ? 0 : -1;
        }
        search.TextChanged += (_, _) => Filter();
        Filter();
        Loaded += (_, _) => search.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter) { e.Handled = true; ExecuteSelected(); }
            else if (e.Key is Key.Down or Key.Up)
            {
                e.Handled = true;
                if (_results.Items.Count == 0) return;
                _results.SelectedIndex = Math.Clamp(_results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _results.Items.Count - 1);
                _results.ScrollIntoView(_results.SelectedItem);
            }
        };
        _results.MouseDoubleClick += (_, _) => ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (_results.SelectedItem is not Entry entry || !entry.Command.CanExecute(null)) return;
        Close();
        entry.Command.Execute(null);
    }
}
