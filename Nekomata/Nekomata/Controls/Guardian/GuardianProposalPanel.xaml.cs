using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Actions;

namespace Nekomata.UI.Controls.Guardian;

public partial class GuardianProposalPanel : UserControl
{
    public GuardianProposalPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshSummary();
    }

    public static readonly DependencyProperty ProposalProperty = DependencyProperty.Register(
        nameof(Proposal),
        typeof(GuardianActionResponse),
        typeof(GuardianProposalPanel),
        new PropertyMetadata(null, (dependencyObject, _) =>
            ((GuardianProposalPanel)dependencyObject).RefreshSummary()));

    public GuardianActionResponse? Proposal
    {
        get => (GuardianActionResponse?)GetValue(ProposalProperty);
        set => SetValue(ProposalProperty, value);
    }

    public static readonly DependencyProperty ProjectNameProperty = DependencyProperty.Register(
        nameof(ProjectName), typeof(string), typeof(GuardianProposalPanel), new PropertyMetadata(string.Empty));

    public string ProjectName
    {
        get => (string)GetValue(ProjectNameProperty);
        set => SetValue(ProjectNameProperty, value);
    }

    public static readonly DependencyProperty DismissCommandProperty = DependencyProperty.Register(
        nameof(DismissCommand), typeof(ICommand), typeof(GuardianProposalPanel), new PropertyMetadata(null));

    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public static readonly DependencyProperty RegenerateCommandProperty = DependencyProperty.Register(
        nameof(RegenerateCommand), typeof(ICommand), typeof(GuardianProposalPanel), new PropertyMetadata(null));

    public ICommand? RegenerateCommand
    {
        get => (ICommand?)GetValue(RegenerateCommandProperty);
        set => SetValue(RegenerateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateTasksCommandProperty = DependencyProperty.Register(
        nameof(CreateTasksCommand), typeof(ICommand), typeof(GuardianProposalPanel), new PropertyMetadata(null));

    public ICommand? CreateTasksCommand
    {
        get => (ICommand?)GetValue(CreateTasksCommandProperty);
        set => SetValue(CreateTasksCommandProperty, value);
    }

    private void SelectAllClick(object sender, RoutedEventArgs e) => SetAllSelected(true);

    private void ClearAllClick(object sender, RoutedEventArgs e) => SetAllSelected(false);

    private void SetAllSelected(bool selected)
    {
        if (Proposal is null)
            return;

        foreach (var task in Proposal.Tasks)
            task.Selected = selected;
        foreach (var change in Proposal.Changes)
            change.Selected = selected;

        // The proposal records are intentionally lightweight POCOs, so force
        // the item templates to refresh after a bulk selection operation.
        ChangesItems.Items.Refresh();
        TasksItems.Items.Refresh();
        RefreshSummary();
    }

    private void ProposalSelectionChanged(object sender, RoutedEventArgs e) => RefreshSummary();

    private void ProposalValueChanged(object sender, RoutedEventArgs e) => RefreshSummary();

    private void RefreshSummary()
    {
        if (!IsInitialized || Proposal is null)
            return;

        var impact = GuardianProposalImpact.From(Proposal);

        SelectedCountText.Text = $"{impact.SelectedCount} of {impact.TotalCount}";
        EffortText.Text = impact.EstimatedMinutes < 60
            ? $"{impact.EstimatedMinutes} mins"
            : $"{impact.EstimatedMinutes / 60}h {impact.EstimatedMinutes % 60}m";
        ValueText.Text = impact.EstimatedBusinessValue.ToString("C0", CultureInfo.CurrentCulture);
        ChangesSection.Visibility = Proposal.Changes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TasksSection.Visibility = Proposal.Tasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var impacts = new List<string>();
        if (impact.NewTaskCount > 0) impacts.Add($"{impact.NewTaskCount} new task{Plural(impact.NewTaskCount)}");
        if (impact.TaskChangeCount > 0) impacts.Add($"{impact.TaskChangeCount} task update{Plural(impact.TaskChangeCount)}");
        if (impact.ProjectChangeCount > 0) impacts.Add($"{impact.ProjectChangeCount} project update{Plural(impact.ProjectChangeCount)}");
        if (impact.CalendarChangeCount > 0) impacts.Add($"{impact.CalendarChangeCount} calendar block{Plural(impact.CalendarChangeCount)}");
        ImpactText.Text = impacts.Count == 0
            ? "Nothing selected. Choose one or more proposals to continue."
            : "Applying will create or update " + string.Join(", ", impacts) + ".";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
