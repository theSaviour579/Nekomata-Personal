using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.Controls;

public partial class GuardianContextBriefingCard : UserControl
{
    public GuardianContextBriefingCard() => InitializeComponent();

    public static readonly DependencyProperty BriefingProperty = DependencyProperty.Register(
        nameof(Briefing), typeof(GuardianContextBriefing), typeof(GuardianContextBriefingCard),
        new PropertyMetadata(new GuardianContextBriefing()));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(GuardianContextBriefingCard));

    public GuardianContextBriefing Briefing
    {
        get => (GuardianContextBriefing)GetValue(BriefingProperty);
        set => SetValue(BriefingProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
