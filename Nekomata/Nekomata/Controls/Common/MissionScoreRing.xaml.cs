using System.Windows;
using System.Windows.Controls;

namespace Nekomata.UI.Controls.Common;

public partial class MissionScoreRing : UserControl
{
    public MissionScoreRing()
    {
        InitializeComponent();
    }

    public int Score
    {
        get => (int)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(
            nameof(Score),
            typeof(int),
            typeof(MissionScoreRing),
            new PropertyMetadata(0));
}