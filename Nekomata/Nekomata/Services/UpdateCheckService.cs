using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.IO;

namespace Nekomata.UI.Services;

public sealed record UpdateCheckResult(bool Configured, bool UpdateAvailable, Version CurrentVersion,
    Version? LatestVersion, string Status, string? ReleaseUrl);

public sealed class UpdateCheckService
{
    private const string Repository = "theSaviour579/Nekomata";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        var gh = FindGitHubCli();
        if (gh is null)
            return new(false, false, current, null, "Update checks require the authenticated GitHub CLI because this repository is private.", null);

        var psi = new ProcessStartInfo(gh)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("api");
        psi.ArgumentList.Add($"repos/{Repository}/releases/latest");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start GitHub CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        _ = await stderr;
        if (process.ExitCode != 0)
            return new(false, false, current, null, "Sign in with 'gh auth login' to check private Nekomata releases.", null);

        using var document = JsonDocument.Parse(await stdout);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v');
        var url = root.GetProperty("html_url").GetString();
        if (!Version.TryParse(tag?.Split('-', 2)[0], out var latest))
            return new(true, false, current, null, "The latest release has an unsupported version label.", url);

        var available = latest > current;
        return new(true, available, current, latest,
            available ? $"Nekomata {latest} is available (installed: {current.ToString(3)})." : $"Nekomata {current.ToString(3)} is up to date.", url);
    }

    private static string? FindGitHubCli()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path.Trim(), "gh.exe"));
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
        return candidates.Append(standard).FirstOrDefault(File.Exists);
    }
}
