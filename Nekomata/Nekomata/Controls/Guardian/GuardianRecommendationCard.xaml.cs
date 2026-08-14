using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nekomata.UI.Controls.Guardian;

public partial class GuardianRecommendationCard : UserControl
{
    public GuardianRecommendationCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty
        AskGuardianCommandProperty =
            DependencyProperty.Register(
                nameof(AskGuardianCommand),
                typeof(ICommand),
                typeof(GuardianRecommendationCard),
                new PropertyMetadata(null));

    public ICommand? AskGuardianCommand
    {
        get => (ICommand?)GetValue(AskGuardianCommandProperty);
        set => SetValue(AskGuardianCommandProperty, value);
    }

    public static readonly DependencyProperty
        StartMissionCommandProperty =
            DependencyProperty.Register(
                nameof(StartMissionCommand),
                typeof(ICommand),
                typeof(GuardianRecommendationCard),
                new PropertyMetadata(null));

    public ICommand? StartMissionCommand
    {
        get => (ICommand?)GetValue(StartMissionCommandProperty);
        set => SetValue(StartMissionCommandProperty, value);
    }
}