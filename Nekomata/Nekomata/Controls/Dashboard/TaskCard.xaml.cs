using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nekomata.UI.Controls.Dashboard;

public partial class TaskCard : UserControl
{
    public TaskCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty CompleteCommandProperty =
        DependencyProperty.Register(
            nameof(CompleteCommand),
            typeof(ICommand),
            typeof(TaskCard),
            new PropertyMetadata(null));

    public ICommand? CompleteCommand
    {
        get => (ICommand?)GetValue(CompleteCommandProperty);
        set => SetValue(CompleteCommandProperty, value);
    }
}