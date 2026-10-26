using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Windows;

// Heads-up overlay: a slim, translucent, always-on-top strip that keeps Guardian present
// over whatever the user is working in - the presence orb, what's next on the calendar, the
// active focus mission, the nearest task deadline and how many items need you. Built in
// code (no XAML) because it is a single strip of bound text. Drag to move; double-click
// to bring Nekomata forward; right-click to hide. It never takes focus.
public sealed class GuardianHudWindow : Window
{
    private static readonly Color Lightning = Color.FromRgb(0x42, 0xD9, 0xFF);
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)));
    private static readonly Brush MutedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8F, 0xA4, 0xB8)));
    private static readonly Brush DividerBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0x42, 0xD9, 0xFF)));

    public GuardianHudWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        Title = "Guardian heads-up";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;

        var orb = new GuardianPresenceOrb();
        orb.SetBinding(GuardianPresenceOrb.StateProperty, new Binding(nameof(MainViewModel.GuardianPresenceState)));

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Viewbox { Width = 34, Height = 34, Child = orb, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(Segment("NEXT", nameof(MainViewModel.HudNextText), null));
        row.Children.Add(Segment("FOCUS", nameof(MainViewModel.HudMissionText), nameof(MainViewModel.HasHudMission)));
        row.Children.Add(Segment("PLAN", nameof(MainViewModel.HudPlanText), nameof(MainViewModel.HasHudPlan),
            urgentPath: nameof(MainViewModel.HudOffPlan)));
        row.Children.Add(Segment("HALO", nameof(MainViewModel.HudDeadlineText), nameof(MainViewModel.HasHudDeadline),
            urgentPath: nameof(MainViewModel.HudDeadlineUrgent)));
        row.Children.Add(Segment("", nameof(MainViewModel.HudAttentionText), nameof(MainViewModel.HasHudAttention),
            accent: true));
        row.Children.Add(Segment("", nameof(MainViewModel.HudQuietText), nameof(MainViewModel.HasHudQuiet)));
        var thought = (FrameworkElement)Segment("THINKING", nameof(MainViewModel.GuardianThought), null);
        thought.SetBinding(ToolTipProperty, new Binding(nameof(MainViewModel.GuardianThoughtHistory)));
        row.Children.Add(thought);

        Content = new Border
        {
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromArgb(0xDE, 0x07, 0x0B, 0x11)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x42, 0xD9, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 18, 5),
            Effect = new DropShadowEffect { Color = Lightning, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.35 },
            Child = row,
            Cursor = Cursors.SizeAll,
            ToolTip = "Guardian heads-up · drag to move · double-click to open Nekomata · right-click to hide"
        };

        var menu = new ContextMenu();
        var hide = new MenuItem { Header = "Hide heads-up (Ctrl+Alt+H)" };
        hide.Click += (_, _) => viewModel.SetHudVisible(false);
        menu.Items.Add(hide);
        ContextMenu = menu;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                var main = Application.Current.MainWindow;
                if (main is null) return;
                if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
                main.Show(); main.Activate();
                return;
            }
            try { DragMove(); } catch (InvalidOperationException) { /* button released before the drag began */ }
        };
        LocationChanged += (_, _) => viewModel.RememberHudPosition(Left, Top);
    }

    private static UIElement Segment(string label, string textPath, string? visiblePath, string? urgentPath = null, bool accent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Border { Width = 1, Height = 18, Background = DividerBrush, Margin = new Thickness(10, 0, 10, 0) });
        if (label.Length > 0)
            panel.Children.Add(new TextBlock
            {
                Text = label, Foreground = MutedBrush, FontSize = 10, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0)
            });
        // Colour and weight live in a style (not local values) so the urgent trigger can override them.
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, accent ? new SolidColorBrush(Lightning) : TextBrush));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, accent ? FontWeights.SemiBold : FontWeights.Normal));
        if (urgentPath is not null)
        {
            var trigger = new DataTrigger { Binding = new Binding(urgentPath), Value = true };
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x72))));
            trigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(trigger);
        }
        var text = new TextBlock
        {
            Style = style, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        panel.Children.Add(text);
        if (visiblePath is not null)
            panel.SetBinding(VisibilityProperty, new Binding(visiblePath) { Converter = new BooleanToVisibilityConverter() });
        return panel;
    }

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }
}
