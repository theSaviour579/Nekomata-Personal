using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Nekomata.Core.Backup;
using Nekomata.Data.Local;

namespace Nekomata.UI.Services;

public sealed record PersonalBackupResult(bool Success, string Message, string? Path = null);

public sealed class PersonalBackupService(LocalWorkspaceStore workspace, PersonalProfileService profile)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PersonalBackupResult> CreateAsync(string destination, string passphrase, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var archive = TemporaryPath("zip");
        try
        {
            if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Enter a backup password.");
            if (File.Exists(destination)) throw new IOException("A backup with this name already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await workspace.EnsureCreatedAsync();

            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(workspace.FilePath, "workspace.json", CompressionLevel.Optimal);
                if (File.Exists(profile.FilePath)) zip.CreateEntryFromFile(profile.FilePath, "profile.json", CompressionLevel.Optimal);
                var metadata = zip.CreateEntry("backup.json", CompressionLevel.Optimal);
                await using var stream = metadata.Open();
                await JsonSerializer.SerializeAsync(stream, new { format = 1, createdAt = DateTimeOffset.UtcNow, product = "Nekomata Personal" }, cancellationToken: ct);
            }

            await BackupEncryption.EncryptAsync(archive, destination, passphrase, ct);
            return new(true, $"Encrypted Personal backup created: {Path.GetFileName(destination)}", destination);
        }
        catch (Exception ex) { return new(false, ex.Message); }
        finally { DeleteTemporary(archive); _gate.Release(); }
    }

    public async Task<PersonalBackupResult> RestoreAsync(string source, string passphrase, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var archive = TemporaryPath("zip");
        try
        {
            await BackupEncryption.DecryptAsync(source, archive, passphrase, ct);
            using var zip = ZipFile.OpenRead(archive);
            var workspaceEntry = zip.GetEntry("workspace.json") ?? throw new InvalidDataException("This is not a valid Nekomata Personal backup.");
            var profileEntry = zip.GetEntry("profile.json");

            await ReplaceFromEntryAsync(workspaceEntry, workspace.FilePath, ct);
            if (profileEntry is not null) await ReplaceFromEntryAsync(profileEntry, profile.FilePath, ct);
            profile.Reload();
            return new(true, "Personal workspace restored successfully.", source);
        }
        catch (Exception ex) { return new(false, ex.Message); }
        finally { DeleteTemporary(archive); _gate.Release(); }
    }

    private static async Task ReplaceFromEntryAsync(ZipArchiveEntry entry, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".restore";
        try
        {
            await using (var source = entry.Open())
            await using (var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(target, ct);
            File.Move(temporary, destination, true);
        }
        finally { DeleteTemporary(temporary); }
    }

    private static string TemporaryPath(string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Nekomata Personal", "BackupWork");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Guid.NewGuid():N}.{extension}");
    }

    private static void DeleteTemporary(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
