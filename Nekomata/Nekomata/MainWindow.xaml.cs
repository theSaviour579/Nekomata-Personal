using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using Nekomata.UI.ViewModels;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Nekomata.UI;

public partial class MainWindow : Window
{
    private const int QuickCaptureHotKeyId = 0x4E4B;
    private const int TalkToGuardianHotKeyId = 0x4E4C;
    private const int ToggleHudHotKeyId = 0x4E4D;
    private const int QuickNoteHotKeyId = 0x4E4E;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkSpace = 0x20;
    private const uint VkH = 0x48;
    private const uint VkN = 0x4E;
    private HwndSource? _windowSource;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OpenQuickMenu();
            }
        };

        viewModel.ChatHistory.CollectionChanged += ChatHistory_CollectionChanged;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        SourceInitialized += RegisterQuickCaptureHotKey;
        Closed += UnregisterQuickCaptureHotKey;
    }

    private void RegisterQuickCaptureHotKey(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        if (!RegisterHotKey(handle, QuickCaptureHotKeyId, ModControl | ModShift, VkSpace))
            System.Diagnostics.Debug.WriteLine("Ctrl+Shift+Space is already registered by another application; Nekomata will retain its in-window shortcut.");
        if (!RegisterHotKey(handle, TalkToGuardianHotKeyId, ModControl | ModAlt, VkSpace))
            System.Diagnostics.Debug.WriteLine("Ctrl+Alt+Space is already registered by another application; Nekomata will retain its in-window shortcut.");
        if (!RegisterHotKey(handle, ToggleHudHotKeyId, ModControl | ModAlt, VkH))
            System.Diagnostics.Debug.WriteLine("Ctrl+Alt+H is already registered by another application; Nekomata will retain its in-window shortcut.");
        if (!RegisterHotKey(handle, QuickNoteHotKeyId, ModControl | ModAlt, VkN))
            System.Diagnostics.Debug.WriteLine("Ctrl+Alt+N is already registered by another application; the quick note box won't open from other apps.");
    }

    private void UnregisterQuickCaptureHotKey(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, QuickCaptureHotKeyId);
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, TalkToGuardianHotKeyId);
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, ToggleHudHotKeyId);
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, QuickNoteHotKeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotKey) return IntPtr.Zero;
        if (DataContext is not MainViewModel viewModel) return IntPtr.Zero;
        var hotKeyId = wParam.ToInt32();
        if (hotKeyId == QuickCaptureHotKeyId)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate(); Topmost = true; Topmost = false;
            viewModel.OpenQuickCaptureCommand.Execute(null);
            handled = true;
        }
        else if (hotKeyId == QuickNoteHotKeyId)
        {
            // Stay in the app the user is using: only the small note box comes forward.
            new Nekomata.UI.Windows.QuickNoteWindow(viewModel.FileQuickNoteAsync).Show();
            handled = true;
        }
        else if (hotKeyId == ToggleHudHotKeyId)
        {
            // The overlay is for staying in other apps, so don't pull Nekomata forward.
            viewModel.ToggleHudCommand.Execute(null);
            handled = true;
        }
        else if (hotKeyId == TalkToGuardianHotKeyId)
        {
            // Remember the window the user was using before Nekomata covers it, so "this" means that window.
            try { viewModel.CaptureScreenContext(); } catch { /* the look is optional */ }
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate(); Topmost = true; Topmost = false;
            viewModel.TalkToGuardianCommand.Execute(null);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CommandPaletteVisible) ||
            sender is not MainViewModel { CommandPaletteVisible: true }) return;
        Dispatcher.BeginInvoke(() =>
        {
            CommandPaletteTextBox.Focus();
            CommandPaletteTextBox.SelectAll();
        });
    }

    private void QuickMenu_Click(object sender, RoutedEventArgs e) => OpenQuickMenu();

    private void OpenQuickMenu()
    {
        if (DataContext is MainViewModel model)
            new Windows.CommandMenuWindow(model) { Owner = this }.ShowDialog();
    }

    private void ChatHistory_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            GuardianScrollViewer.ScrollToEnd();
        });
    }
}
