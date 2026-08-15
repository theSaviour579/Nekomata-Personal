using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nekomata.UI.Services;

public sealed record UpdateCheckResult(
    bool Configured,
    bool UpdateAvailable,
    Version CurrentVersion,
    Version? LatestVersion,
    string Status,
    string? ReleaseUrl,
    string? Tag = null,
    string? InstallerName = null,
    string? InstallerUrl = null,
    string? InstallerDigest = null,
    bool RequiresGitHubCli = false);

public sealed record UpdateDownloadResult(bool Success, string Message, string? InstallerPath = null);

public sealed class UpdateCheckService
{
    private const string Repository = "theSaviour579/Nekomata-Personal-Releases";
    private static readonly HttpClient Http = CreateHttpClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        var (json, requiresCli) = await GetLatestReleaseJsonAsync(ct);
        if (json is null)
            return new(false, false, current, null,
                "Update access is not configured. Private releases require an authenticated GitHub CLI session.", null);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString();
        var versionText = tagName?.TrimStart('v').Split('-', 2)[0];
        var releaseUrl = root.GetProperty("html_url").GetString();
        if (!Version.TryParse(versionText, out var latest))
            return new(true, false, current, null, "The latest release has an unsupported version label.", releaseUrl);

        var installer = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(asset => asset.GetProperty("name").GetString()?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
                && asset.GetProperty("name").GetString()?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true);
        var installerName = installer.ValueKind == JsonValueKind.Undefined ? null : installer.GetProperty("name").GetString();
        var installerUrl = installer.ValueKind == JsonValueKind.Undefined ? null : installer.GetProperty("browser_download_url").GetString();
        var digest = installer.ValueKind == JsonValueKind.Undefined || !installer.TryGetProperty("digest", out var digestElement)
            ? null : digestElement.GetString();
        var available = latest > current && installerName is not null;

        return new(true, available, current, latest,
            available
                ? $"Nekomata Personal {latest} is ready to install (installed: {current.ToString(3)})."
                : latest > current
                    ? $"Nekomata Personal {latest} is available, but its Windows installer is missing."
                    : $"Nekomata Personal {current.ToString(3)} is up to date.",
            releaseUrl, tagName, installerName, installerUrl, digest, requiresCli);
    }

    public async Task<UpdateDownloadResult> DownloadInstallerAsync(
        UpdateCheckResult update,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!update.UpdateAvailable || update.Tag is null || update.InstallerName is null)
            return new(false, "No downloadable update is available.");
        if (string.IsNullOrWhiteSpace(update.InstallerDigest) ||
            !update.InstallerDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return new(false, "The release does not include a verifiable SHA-256 digest, so it was not downloaded.");

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nekomata Personal", "Updates", update.Tag);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(update.InstallerName));
        if (File.Exists(destination)) File.Delete(destination);

        try
        {
            progress?.Report("Downloading the verified Windows installer…");
            if (update.RequiresGitHubCli)
                await DownloadWithGitHubCliAsync(update, directory, ct);
            else
                await DownloadWithHttpAsync(update.InstallerUrl ?? throw new InvalidOperationException("The installer URL is missing."), destination, ct);

            progress?.Report("Verifying the installer checksum…");
            await using var installerStream = File.OpenRead(destination);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, ct));
            var expected = update.InstallerDigest["sha256:".Length..];
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destination);
                return new(false, "The downloaded installer failed its SHA-256 verification and was removed.");
            }

            return new(true, $"Nekomata Personal {update.LatestVersion} is downloaded and verified.", destination);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(destination)) File.Delete(destination); } catch { }
            return new(false, "Update download failed: " + ex.GetBaseException().Message);
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        var start = new ProcessStartInfo(installerPath) { UseShellExecute = true };
        start.ArgumentList.Add("/SILENT");
        start.ArgumentList.Add("/SUPPRESSMSGBOXES");
        start.ArgumentList.Add("/CLOSEAPPLICATIONS");
        start.ArgumentList.Add("/RESTARTAPPLICATIONS");
        Process.Start(start);
    }

    private static async Task<(string? Json, bool RequiresCli)> GetLatestReleaseJsonAsync(CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
            if (response.IsSuccessStatusCode) return (await response.Content.ReadAsStringAsync(ct), false);
        }
        catch (HttpRequestException) { }

        var gh = FindGitHubCli();
        if (gh is null) return (null, false);
        var result = await RunGitHubCliAsync(gh, ["api", $"repos/{Repository}/releases/latest"], ct);
        return result.ExitCode == 0 ? (result.Output, true) : (null, true);
    }

    private static async Task DownloadWithHttpAsync(string url, string destination, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct);
    }

    private static async Task DownloadWithGitHubCliAsync(UpdateCheckResult update, string directory, CancellationToken ct)
    {
        var gh = FindGitHubCli() ?? throw new InvalidOperationException("GitHub CLI is required to download this private release.");
        var result = await RunGitHubCliAsync(gh,
            ["release", "download", update.Tag!, "--repo", Repository, "--pattern", update.InstallerName!, "--dir", directory, "--clobber"], ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "GitHub rejected the download." : result.Error.Trim());
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitHubCliAsync(
        string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start GitHub CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, await stdout, await stderr);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Nekomata-Personal-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string? FindGitHubCli()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path.Trim(), "gh.exe"));
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe");
        return candidates.Append(standard).FirstOrDefault(File.Exists);
    }
}
