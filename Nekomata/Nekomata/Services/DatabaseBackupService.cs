using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;
using Nekomata.Core.Backup;

namespace Nekomata.UI.Services;

public sealed class DatabaseBackupService
{
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DatabaseBackupService(IConfiguration configuration) => _configuration = configuration;

    public string AutomaticBackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nekomata", "Backups");

    public string? ConfiguredPassphrase => _configuration["Backup:Passphrase"] ??
        Environment.GetEnvironmentVariable("NEKOMATA_BACKUP_PASSPHRASE");

    public async Task<BackupStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var pgDump = FindTool("pg_dump.exe");
        var pgRestore = FindTool("pg_restore.exe");
        var files = Directory.Exists(AutomaticBackupDirectory)
            ? Directory.GetFiles(AutomaticBackupDirectory, "*.nkb").Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTime).ToList()
            : [];
        var available = pgDump is not null && pgRestore is not null;
        await Task.CompletedTask;
        return new(available,
            available ? $"PostgreSQL tools found in {Path.GetDirectoryName(pgDump)}" : "pg_dump and pg_restore were not found. Install PostgreSQL client tools or set Backup:PostgreSqlBinPath.",
            files.FirstOrDefault()?.LastWriteTime, files.FirstOrDefault()?.FullName,
            !string.IsNullOrWhiteSpace(ConfiguredPassphrase));
    }

    public async Task<BackupOperationResult> EnsureDailyBackupAsync(CancellationToken ct = default)
    {
        var passphrase = ConfiguredPassphrase;
        if (string.IsNullOrWhiteSpace(passphrase))
            return new(false, "Automatic backup is waiting for Backup:Passphrase or NEKOMATA_BACKUP_PASSPHRASE.");
        var status = await GetStatusAsync(ct);
        if (status.IsFresh) return new(true, status.FreshnessLabel, status.LatestBackupPath);
        Directory.CreateDirectory(AutomaticBackupDirectory);
        var path = Path.Combine(AutomaticBackupDirectory, $"nekomata-{DateTime.Now:yyyyMMdd-HHmmss}.nkb");
        var result = await CreateBackupAsync(path, passphrase, ct);
        if (result.Success) ApplyRetention();
        return result;
    }

    public async Task<BackupOperationResult> CreateBackupAsync(string destination, string passphrase, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var temp = TemporaryDumpPath();
        try
        {
            var pgDump = FindTool("pg_dump.exe") ?? throw new FileNotFoundException("pg_dump was not found. Install PostgreSQL client tools or configure Backup:PostgreSqlBinPath.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await RunToolAsync(pgDump, ["--format=custom", "--schema=assistant", "--no-owner", "--no-privileges", "--file", temp], ct);
            await VerifyDumpAsync(temp, ct);
            if (File.Exists(destination)) throw new IOException("A backup with this name already exists.");
            await BackupEncryption.EncryptAsync(temp, destination, passphrase, ct);
            return new(true, $"Encrypted backup created: {Path.GetFileName(destination)}", destination);
        }
        catch (Exception ex) { return new(false, ex.Message); }
        finally { DeleteTemporary(temp); _gate.Release(); }
    }

    public async Task<BackupOperationResult> RestoreAsync(string source, string passphrase, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var temp = TemporaryDumpPath();
        var safetyTemp = TemporaryDumpPath();
        try
        {
            var pgRestore = FindTool("pg_restore.exe") ?? throw new FileNotFoundException("pg_restore was not found. Install PostgreSQL client tools or configure Backup:PostgreSqlBinPath.");
            var pgDump = FindTool("pg_dump.exe") ?? throw new FileNotFoundException("pg_dump was not found; a pre-restore safety backup cannot be created.");
            await BackupEncryption.DecryptAsync(source, temp, passphrase, ct);
            await VerifyDumpAsync(temp, ct);

            Directory.CreateDirectory(AutomaticBackupDirectory);
            var safetyPath = Path.Combine(AutomaticBackupDirectory, $"pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}.nkb");
            await RunToolAsync(pgDump, ["--format=custom", "--schema=assistant", "--no-owner", "--no-privileges", "--file", safetyTemp], ct);
            await VerifyDumpAsync(safetyTemp, ct);
            await BackupEncryption.EncryptAsync(safetyTemp, safetyPath, passphrase, ct);

            await RunToolAsync(pgRestore, ["--clean", "--if-exists", "--schema=assistant", "--no-owner", "--no-privileges", "--exit-on-error", temp], ct);
            ApplyRetention();
            return new(true, $"Backup restored successfully. A safety copy of the previous database was saved as {Path.GetFileName(safetyPath)}. Restart Nekomata before making further changes.", source);
        }
        catch (Exception ex) { return new(false, ex.Message); }
        finally { DeleteTemporary(temp); DeleteTemporary(safetyTemp); _gate.Release(); }
    }

    public async Task VerifyDumpAsync(string dumpPath, CancellationToken ct = default)
    {
        var pgRestore = FindTool("pg_restore.exe") ?? throw new FileNotFoundException("pg_restore was not found.");
        var output = await RunToolAsync(pgRestore, ["--list", dumpPath], ct, includeDatabaseArguments: false);
        if (!output.Contains("assistant", StringComparison.OrdinalIgnoreCase) ||
            !output.Contains("schema_migrations", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup verification failed: the assistant schema or migration history is missing.");
    }

    private async Task<string> RunToolAsync(string executable, IEnumerable<string> arguments, CancellationToken ct, bool includeDatabaseArguments = true)
    {
        var database = _configuration.GetSection("Database");
        var psi = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        if (includeDatabaseArguments)
        {
            psi.ArgumentList.Add("--host"); psi.ArgumentList.Add(database["Host"] ?? "localhost");
            psi.ArgumentList.Add("--port"); psi.ArgumentList.Add(database["Port"] ?? "5432");
            psi.ArgumentList.Add("--username"); psi.ArgumentList.Add(database["Username"] ?? "postgres");
            psi.ArgumentList.Add("--dbname"); psi.ArgumentList.Add(database["Database"] ?? "nekomata");
            psi.Environment["PGPASSWORD"] = database["Password"] ?? string.Empty;
        }
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(SafeToolError(error, process.ExitCode));
        return output;
    }

    private string? FindTool(string name)
    {
        var configured = _configuration["Backup:PostgreSqlBinPath"];
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, name))) return Path.Combine(configured, name);
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(part => Path.Combine(part.Trim(), name)).FirstOrDefault(File.Exists);
        if (path is not null) return path;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateDirectories(root)
            .Select(versionDirectory => Path.Combine(versionDirectory, "bin", name))
            .Where(File.Exists)
            .OrderByDescending(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private void ApplyRetention()
    {
        var files = Directory.GetFiles(AutomaticBackupDirectory, "*.nkb").Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTime).ToList();
        var dailyRetention = Math.Clamp(_configuration.GetValue("Backup:DailyRetention", 7), 1, 31);
        var weeklyRetention = Math.Clamp(_configuration.GetValue("Backup:WeeklyRetention", 4), 0, 52);
        var keep = files.Take(dailyRetention).Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var weekly in files.Skip(dailyRetention).GroupBy(file => (file.LastWriteTime.Year, Week: System.Globalization.ISOWeek.GetWeekOfYear(file.LastWriteTime))).Take(weeklyRetention))
            keep.Add(weekly.First().FullName);
        foreach (var file in files.Where(file => !keep.Contains(file.FullName))) file.Delete();
    }

    private static string TemporaryDumpPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Nekomata", "BackupWork");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.dump");
    }

    private static void DeleteTemporary(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    private static string SafeToolError(string error, int exitCode) => string.IsNullOrWhiteSpace(error) ? $"PostgreSQL tool exited with code {exitCode}." : error.Trim();
}
