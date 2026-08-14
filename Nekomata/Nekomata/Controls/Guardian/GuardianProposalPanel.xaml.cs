using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nekomata.AI.Models.Actions;

namespace Nekomata.UI.Controls.Guardian;

public partial class GuardianProposalPanel : UserControl
{
    public GuardianProposalPanel()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"CreateTasksCommand is {(CreateTasksCommand is null ? "NULL" : "SET")}");
        };
    }

    public static readonly DependencyProperty ProposalProperty =
        DependencyProperty.Register(
            nameof(Proposal),
            typeof(GuardianActionResponse),
            typeof(GuardianProposalPanel),
            new PropertyMetadata(null));

    public GuardianActionResponse? Proposal
    {
        get => (GuardianActionResponse?)GetValue(ProposalProperty);
        set => SetValue(ProposalProperty, value);
    }

    public static readonly DependencyProperty ProjectNameProperty =
        DependencyProperty.Register(
            nameof(ProjectName),
            typeof(string),
            typeof(GuardianProposalPanel),
            new PropertyMetadata("Guardian Proposal"));

    public string ProjectName
    {
        get => (string)GetValue(ProjectNameProperty);
        set => SetValue(ProjectNameProperty, value);
    }

    public static readonly DependencyProperty DismissCommandProperty =
        DependencyProperty.Register(
            nameof(DismissCommand),
            typeof(ICommand),
            typeof(GuardianProposalPanel),
            new PropertyMetadata(null));

    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    public static readonly DependencyProperty RegenerateCommandProperty =
        DependencyProperty.Register(
            nameof(RegenerateCommand),
            typeof(ICommand),
            typeof(GuardianProposalPanel),
            new PropertyMetadata(null));

    public ICommand? RegenerateCommand
    {
        get => (ICommand?)GetValue(RegenerateCommandProperty);
        set => SetValue(RegenerateCommandProperty, value);
    }

    public static readonly DependencyProperty CreateTasksCommandProperty =
        DependencyProperty.Register(
            nameof(CreateTasksCommand),
            typeof(ICommand),
            typeof(GuardianProposalPanel),
            new PropertyMetadata(null));

    public ICommand? CreateTasksCommand
    {
        get => (ICommand?)GetValue(CreateTasksCommandProperty);
        set => SetValue(CreateTasksCommandProperty, value);
    }
}