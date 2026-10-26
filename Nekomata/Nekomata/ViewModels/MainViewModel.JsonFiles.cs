using System.IO;
using System.Text.Json;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    /// <summary>Where Nekomata Personal keeps its own files: %LocalAppData%\Nekomata Personal.</summary>
    private static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata Personal");

    /// <summary>Opens a file Guardian wrote (a Word document, say) in its default app.</summary>
    private static void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Couldn't open {path}: {ex.Message}"); }
    }

    /// <summary>Reads one of Guardian's small local JSON files, or default when it is missing or unreadable.</summary>
    private static T? LoadJson<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{Path.GetFileName(path)} unreadable: {ex.Message}"); return default; }
    }

    /// <summary>Writes one of Guardian's small local JSON files atomically.</summary>
    private static void SaveJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{Path.GetFileName(path)} not saved: {ex.Message}"); }
    }
}
