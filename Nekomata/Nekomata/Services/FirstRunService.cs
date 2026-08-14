using System.IO;

namespace Nekomata.UI.Services;

public sealed class FirstRunService
{
    private readonly string _marker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata", "setup-complete-v1");

    public bool IsFirstRun => !File.Exists(_marker);

    public void Complete()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_marker)!);
        File.WriteAllText(_marker, DateTime.UtcNow.ToString("O"));
    }
}
