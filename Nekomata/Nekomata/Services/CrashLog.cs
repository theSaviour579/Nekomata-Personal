using System.IO;
using System.Windows;

namespace Nekomata.UI.Services;

/// <summary>
/// Last line of defence. An error nobody caught is written to %LOCALAPPDATA%\Nekomata\crash.log with its full stack;
/// errors on the UI thread (a window that fails to open, a button handler that throws) are then shown briefly and the
/// app keeps running instead of closing. Only an error outside the UI thread can still end the process, and that is
/// logged first.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();
    private static DateTime _lastShown = DateTime.MinValue;

    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal", "crash.log");

    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += (_, args) =>
        {
            Write("UI", args.Exception);
            args.Handled = true;
            Show(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write(args.IsTerminating ? "Fatal" : "Background", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write("Background task", args.Exception);
            args.SetObserved();
        };
    }

    public static void Write(string where, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 2_000_000) File.Move(Path, Path + ".old", true);
                File.AppendAllText(Path, $"===== {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · {where} · v{typeof(CrashLog).Assembly.GetName().Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* never throw from the crash handler */ }
    }

    private static void Show(Exception ex)
    {
        if (DateTime.Now - _lastShown < TimeSpan.FromSeconds(30)) return;
        _lastShown = DateTime.Now;
        try
        {
            MessageBox.Show($"Something went wrong, but Nekomata is still running.\n\n{ex.GetBaseException().Message}\n\nDetails are in {Path}.",
                "Nekomata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { }
    }
}
