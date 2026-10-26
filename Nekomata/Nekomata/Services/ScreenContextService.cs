using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nekomata.UI.Services;

public sealed record ScreenSnapshot(byte[] Png, string Title, string App, DateTimeOffset At, IntPtr Window = default);

/// <summary>
/// Captures one window as a PNG so Guardian can look at "this". Only ever the window the user was using (never the whole
/// screen, never Nekomata itself), only in memory, and only sent to the AI when he asks about it.
/// </summary>
public static class ScreenContextService
{
    private const int MaxWidth = 1800;

    public static IntPtr Foreground() => GetForegroundWindow();

    public static bool IsOwnWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return true;
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static ScreenSnapshot? Capture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd) || !IsWindow(hwnd) || IsIconic(hwnd)) return null;
        if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Rect rect, Marshal.SizeOf<Rect>()) != 0 &&
            !GetWindowRect(hwnd, out rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 50 || height < 50) return null;

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            // PrintWindow asks the app to draw itself (works when partly covered); fall back to copying the screen.
            if (!PrintWindow(hwnd, memory, 2 /* PW_RENDERFULLCONTENT */))
                BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020 /* SRCCOPY */);
            SelectObject(memory, old);
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            if (source.PixelWidth > MaxWidth)
            {
                var scale = (double)MaxWidth / source.PixelWidth;
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return new ScreenSnapshot(stream.ToArray(), Title(hwnd), AppName(hwnd), DateTimeOffset.Now, hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Window capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>
    /// The text of a window, read on this PC through Windows UI Automation (control names, field values and document
    /// text such as an email body), in reading order. "Screen this" sends this text, with placeholders, instead of a
    /// picture (AI-use policy: a screenshot can't have names taken out). Bounded in time, elements and length.
    /// </summary>
    public static string ReadText(IntPtr hwnd, int maxChars = 12000)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return "";
        var sb = new StringBuilder();
        try
        {
            var root = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
            var walker = System.Windows.Automation.TreeWalker.ControlViewWalker;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<System.Windows.Automation.AutomationElement>();
            stack.Push(root);
            var clock = Stopwatch.StartNew();
            var visited = 0;
            while (stack.Count > 0 && visited++ < 4000 && sb.Length < maxChars && clock.Elapsed < TimeSpan.FromSeconds(4))
            {
                var element = stack.Pop();
                try
                {
                    var pieces = new List<string?> { element.Current.Name };
                    if (element.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out var text))
                        pieces.Add(((System.Windows.Automation.TextPattern)text).DocumentRange.GetText(maxChars));
                    else if (element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var value))
                        pieces.Add(((System.Windows.Automation.ValuePattern)value).Current.Value);
                    foreach (var piece in pieces)
                    {
                        var p = piece?.Trim();
                        if (p is { Length: > 1 } && seen.Add(p)) sb.AppendLine(p);
                    }
                }
                catch { /* an element can vanish while it's read */ }

                var children = new List<System.Windows.Automation.AutomationElement>();
                try
                {
                    for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                        children.Add(child);
                }
                catch { }
                for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Window text not read: {ex.Message}"); }
        var all = sb.ToString();
        return all.Length > maxChars ? all[..maxChars] : all;
    }

    /// <summary>The foreground app's process name and window title, for the time ledger (titles are categorised, never stored).</summary>
    public static (string App, string Title) Describe(IntPtr hwnd) => hwnd == IntPtr.Zero ? ("", "") : (AppName(hwnd), Title(hwnd));

    /// <summary>How long since the last keyboard or mouse input anywhere in the session.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var idleMs = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size; public uint Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static string Title(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static string AppName(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return ""; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint op);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
}
