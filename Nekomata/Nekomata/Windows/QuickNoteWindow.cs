using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nekomata.UI.Windows;

// The quick note box (Ctrl+Alt+N from anywhere): a small one-line box at the top of the screen, over whatever the user is
// working in. Enter hands the text to Guardian, which files it the same way as a Teams note to himself (task, reminder,
// promise, office reminder, fact or question); Esc or clicking away closes it. Nekomata's main window isn't brought
// forward, and focus goes back to the app he was in. Built in code because it's a single text box.
public sealed class QuickNoteWindow : Window
{
    private static readonly Color Lightning = Color.FromRgb(0x42, 0xD9, 0xFF);
    private readonly Func<string, Task> _file;
    private readonly IntPtr _previous;
    private readonly TextBox _box;
    private bool _closing;
    private bool _focusedOnce;

    /// <param name="label">The label above the box.</param>
    /// <param name="passive">Shown without taking focus (Guardian offering, not the user asking): it waits for a click,
    /// so it never captures what he's typing elsewhere, and closes itself after a few minutes if left alone.</param>
    public QuickNoteWindow(Func<string, Task> file, string? label = null, bool passive = false)
    {
        _file = file;
        _previous = GetForegroundWindow();
        Title = "Note for Guardian";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.18;

        var hint = new TextBlock
        {
            Text = label ?? "NOTE FOR GUARDIAN · Enter to file · Esc to close",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA4, 0xB8)), FontSize = 10, Margin = new Thickness(2, 0, 0, 6)
        };
        _box = new TextBox
        {
            FontSize = 16, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x14, 0x21)), Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x42, 0xD9, 0xFF)), CaretBrush = new SolidColorBrush(Lightning),
            MaxLength = 1000, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false
        };
        var panel = new StackPanel();
        panel.Children.Add(hint);
        panel.Children.Add(_box);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x10, 0x1B, 0x2A)), CornerRadius = new CornerRadius(12), Padding = new Thickness(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x42, 0xD9, 0xFF)), BorderThickness = new Thickness(1), Margin = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
            Child = panel
        };

        PreviewKeyDown += OnKey;
        Activated += (_, _) => { _focusedOnce = true; _box.Focus(); Keyboard.Focus(_box); };
        // A passive box stays put until the user has clicked into it; after that, clicking away closes it as usual.
        Deactivated += (_, _) => { if (!passive || _focusedOnce) CloseAndReturn(); };
        if (passive)
        {
            ShowActivated = false;
            var expiry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(4) };
            expiry.Tick += (_, _) => { expiry.Stop(); if (!_focusedOnce) { _closing = true; Close(); } };
            expiry.Start();
        }
        else Loaded += (_, _) => { Activate(); _box.Focus(); Keyboard.Focus(_box); };
    }

    private async void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseAndReturn(); return; }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        var text = _box.Text.Trim();
        CloseAndReturn();
        if (text.Length > 0)
        {
            try { await _file(text); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Quick note not filed: {ex.Message}"); }
        }
    }

    private void CloseAndReturn()
    {
        if (_closing) return;
        _closing = true;
        Close();
        if (_previous != IntPtr.Zero) SetForegroundWindow(_previous);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
