using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nekomata.UI.Controls.Common;

/// <summary>
/// Interaction logic for GuardianPresenceOrb.xaml - Guardian's always-visible "face" in
/// the header: a lit glass sphere with a containment ring and a scanning dial, rather
/// than a status dot, so it reads as deliberate at rest and not just an icon that moves
/// when something happens. Colour and the scan ring's resting opacity are cross-faded
/// here; the looping motion itself (pulses, rotation) lives in Animations.xaml and is
/// switched wholesale per state.
/// </summary>
public partial class GuardianPresenceOrb : UserControl
{
    private static readonly Color HotWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);

    // (mid, edge, scan ring resting opacity) per state. Idle and Thinking share Guardian's
    // own cyan - Thinking is told apart by the scan ring's speed/brightness, not colour.
    private static readonly Color CyanMid = (Color)ColorConverter.ConvertFromString("#42D9FF")!;
    private static readonly Color CyanEdge = (Color)ColorConverter.ConvertFromString("#163A4D")!;
    private static readonly Color PurpleMid = (Color)ColorConverter.ConvertFromString("#7B5DFF")!;
    private static readonly Color PurpleEdge = (Color)ColorConverter.ConvertFromString("#2C1F66")!;
    private static readonly Color GreenMid = (Color)ColorConverter.ConvertFromString("#46F0B4")!;
    private static readonly Color GreenEdge = (Color)ColorConverter.ConvertFromString("#114D3D")!;
    private static readonly Color AmberMid = (Color)ColorConverter.ConvertFromString("#FFC24D")!;
    private static readonly Color AmberEdge = (Color)ColorConverter.ConvertFromString("#5C3D0A")!;

    private static readonly TimeSpan ColorFadeDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan OpacityFadeDuration = TimeSpan.FromMilliseconds(220);

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State),
        typeof(GuardianPresenceState),
        typeof(GuardianPresenceOrb),
        new PropertyMetadata(GuardianPresenceState.Idle, OnStateChanged));

    private Storyboard? _activeStoryboard;

    public GuardianPresenceOrb()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyState(State, animate: false);
    }

    public GuardianPresenceState State
    {
        get => (GuardianPresenceState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GuardianPresenceOrb orb && orb.IsLoaded)
            orb.ApplyState((GuardianPresenceState)e.NewValue, animate: true);
    }

    private void ApplyState(GuardianPresenceState state, bool animate)
    {
        _activeStoryboard?.Stop(this);

        var (mid, edge, scanOpacity, storyboardKey) = state switch
        {
            GuardianPresenceState.Listening => (PurpleMid, PurpleEdge, 0.75, "GuardianListeningPulse"),
            GuardianPresenceState.Thinking => (CyanMid, CyanEdge, 0.95, "GuardianThinkingSweep"),
            GuardianPresenceState.Speaking => (GreenMid, GreenEdge, 0.8, "GuardianSpeakingPulse"),
            GuardianPresenceState.Acting => (AmberMid, AmberEdge, 0.9, "GuardianActingFlash"),
            _ => (CyanMid, CyanEdge, 0.5, "GuardianIdlePulse")
        };

        AnimateColor(CoreStopInner, GradientStop.ColorProperty, HotWhite, animate);
        AnimateColor(CoreStopMid, GradientStop.ColorProperty, mid, animate);
        AnimateColor(CoreStopEdge, GradientStop.ColorProperty, edge, animate);
        AnimateColor(GlowBrush, SolidColorBrush.ColorProperty, mid, animate);
        AnimateColor(ScanBrush, SolidColorBrush.ColorProperty, mid, animate);
        AnimateOpacity(ScanRing, scanOpacity, animate);

        if (TryFindResource(storyboardKey) is Storyboard template)
        {
            _activeStoryboard = template;
            _activeStoryboard.Begin(this, isControllable: true);
        }
    }

    private static void AnimateColor(Animatable target, DependencyProperty colorProperty, Color value, bool animate)
    {
        if (animate)
        {
            target.BeginAnimation(colorProperty, new ColorAnimation(value, ColorFadeDuration));
        }
        else
        {
            target.BeginAnimation(colorProperty, null);
            target.SetValue(colorProperty, value);
        }
    }

    private static void AnimateOpacity(UIElement target, double value, bool animate)
    {
        if (animate)
        {
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(value, OpacityFadeDuration));
        }
        else
        {
            target.BeginAnimation(OpacityProperty, null);
            target.Opacity = value;
        }
    }
}
