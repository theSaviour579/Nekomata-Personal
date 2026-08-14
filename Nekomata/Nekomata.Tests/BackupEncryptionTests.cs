using Nekomata.Core.Backup;
using Xunit;

namespace Nekomata.Tests;

public sealed class BackupEncryptionTests
{
    [Fact]
    public async Task Encrypted_backup_round_trips_without_plaintext_leak()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"nekomata-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.dump");
        var encrypted = Path.Combine(directory, "backup.nkb");
        var restored = Path.Combine(directory, "restored.dump");
        try
        {
            var content = "assistant schema private workspace data";
            await File.WriteAllTextAsync(source, content, ct);
            await BackupEncryption.EncryptAsync(source, encrypted, "correct horse battery staple", ct);
            Assert.DoesNotContain(content, await File.ReadAllTextAsync(encrypted, ct));

            await BackupEncryption.DecryptAsync(encrypted, restored, "correct horse battery staple", ct);
            Assert.Equal(content, await File.ReadAllTextAsync(restored, ct));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Incorrect_password_cannot_decrypt_backup()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"nekomata-backup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.dump");
        var encrypted = Path.Combine(directory, "backup.nkb");
        try
        {
            await File.WriteAllTextAsync(source, "private data", ct);
            await BackupEncryption.EncryptAsync(source, encrypted, "correct horse battery staple", ct);
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                BackupEncryption.DecryptAsync(encrypted, Path.Combine(directory, "bad.dump"), "totally incorrect password", ct));
            Assert.Contains("incorrect or the file is damaged", error.Message);
        }
        finally { Directory.Delete(directory, true); }
    }
}
