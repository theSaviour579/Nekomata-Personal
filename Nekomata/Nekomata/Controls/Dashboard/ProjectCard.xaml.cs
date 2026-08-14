using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nekomata.UI.Controls.Dashboard;

public partial class ProjectCard : UserControl
{
    public ProjectCard()
    {
        InitializeComponent();
    }

    public ICommand EditCommand
    {
        get => (ICommand)GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public static readonly DependencyProperty EditCommandProperty =
        DependencyProperty.Register(
            nameof(EditCommand),
            typeof(ICommand),
            typeof(ProjectCard));

    public ICommand AskGuardianCommand
    {
        get => (ICommand)GetValue(AskGuardianCommandProperty);
        set => SetValue(AskGuardianCommandProperty, value);
    }

    public static readonly DependencyProperty AskGuardianCommandProperty =
        DependencyProperty.Register(
            nameof(AskGuardianCommand),
            typeof(ICommand),
            typeof(ProjectCard));

    public ICommand GenerateTasksCommand
    {
        get => (ICommand)GetValue(GenerateTasksCommandProperty);
        set => SetValue(GenerateTasksCommandProperty, value);
    }

    public static readonly DependencyProperty GenerateTasksCommandProperty =
        DependencyProperty.Register(
            nameof(GenerateTasksCommand),
            typeof(ICommand),
            typeof(ProjectCard));
}