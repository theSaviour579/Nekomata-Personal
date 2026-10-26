using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.Controls;

public partial class GuardianWorkloadForecastCard : UserControl
{
    public GuardianWorkloadForecastCard() => InitializeComponent();
    public static readonly DependencyProperty ForecastProperty = DependencyProperty.Register(
        nameof(Forecast), typeof(GuardianWorkloadForecast), typeof(GuardianWorkloadForecastCard),
        new PropertyMetadata(new GuardianWorkloadForecast()));
    public static readonly DependencyProperty RecoveryCommandProperty = DependencyProperty.Register(
        nameof(RecoveryCommand), typeof(ICommand), typeof(GuardianWorkloadForecastCard));
    public static readonly DependencyProperty SimulationCommandProperty = DependencyProperty.Register(
        nameof(SimulationCommand), typeof(ICommand), typeof(GuardianWorkloadForecastCard));
    public GuardianWorkloadForecast Forecast
    {
        get => (GuardianWorkloadForecast)GetValue(ForecastProperty);
        set => SetValue(ForecastProperty, value);
    }
    public ICommand? RecoveryCommand
    {
        get => (ICommand?)GetValue(RecoveryCommandProperty);
        set => SetValue(RecoveryCommandProperty, value);
    }
    public ICommand? SimulationCommand
    {
        get => (ICommand?)GetValue(SimulationCommandProperty);
        set => SetValue(SimulationCommandProperty, value);
    }
}
