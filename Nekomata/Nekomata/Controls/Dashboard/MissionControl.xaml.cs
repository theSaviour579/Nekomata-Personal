using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nekomata.UI.Controls.Dashboard;

public partial class MissionControl : UserControl
{
    public MissionControl()
    {
        InitializeComponent();
    }

    public string MissionTitle
    {
        get => (string)GetValue(MissionTitleProperty);
        set => SetValue(MissionTitleProperty, value);
    }

    public static readonly DependencyProperty MissionTitleProperty =
        DependencyProperty.Register(
            nameof(MissionTitle),
            typeof(string),
            typeof(MissionControl));

    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(
            nameof(Score),
            typeof(int),
            typeof(MissionControl));

    public decimal BusinessValue
    {
        get => (decimal)GetValue(BusinessValueProperty);
        set => SetValue(BusinessValueProperty, value);
    }

    public static readonly DependencyProperty BusinessValueProperty =
        DependencyProperty.Register(
            nameof(BusinessValue),
            typeof(decimal),
            typeof(MissionControl));

    public string Threat
    {
        get => (string)GetValue(ThreatProperty);
        set => SetValue(ThreatProperty, value);
    }

    public static readonly DependencyProperty ThreatProperty =
        DependencyProperty.Register(
            nameof(Threat),
            typeof(string),
            typeof(MissionControl));

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(
            nameof(Duration),
            typeof(TimeSpan),
            typeof(MissionControl));

    public DateTime? StartBefore
    {
        get => (DateTime?)GetValue(StartBeforeProperty);
        set => SetValue(StartBeforeProperty, value);
    }

    public static readonly DependencyProperty StartBeforeProperty =
        DependencyProperty.Register(
            nameof(StartBefore),
            typeof(DateTime?),
            typeof(MissionControl));

    public ICommand? BeginMissionCommand
    {
        get => (ICommand?)GetValue(BeginMissionCommandProperty);
        set => SetValue(BeginMissionCommandProperty, value);
    }

    public static readonly DependencyProperty BeginMissionCommandProperty =
        DependencyProperty.Register(
            nameof(BeginMissionCommand),
            typeof(ICommand),
            typeof(MissionControl),
            new PropertyMetadata(null));
}