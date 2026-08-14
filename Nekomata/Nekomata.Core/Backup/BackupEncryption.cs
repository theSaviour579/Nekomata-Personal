using System.Security.Cryptography;
using System.Text;

namespace Nekomata.Core.Backup;

public static class BackupEncryption
{
    private static readonly byte[] Magic = "NEKOMATA-BACKUP-1\n"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Iterations = 210_000;

    public static async Task EncryptAsync(string source, string destination, string passphrase, CancellationToken ct = default)
    {
        ValidatePassphrase(passphrase);
        var plain = await File.ReadAllBytesAsync(source, ct);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag, Magic);

        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(Magic, ct);
        await output.WriteAsync(salt, ct);
        await output.WriteAsync(nonce, ct);
        await output.WriteAsync(tag, ct);
        await output.WriteAsync(cipher, ct);
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plain);
    }

    public static async Task DecryptAsync(string source, string destination, string passphrase, CancellationToken ct = default)
    {
        ValidatePassphrase(passphrase);
        var data = await File.ReadAllBytesAsync(source, ct);
        var minimum = Magic.Length + SaltSize + NonceSize + TagSize;
        if (data.Length <= minimum || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a supported Nekomata backup.");

        var offset = Magic.Length;
        var salt = data.AsSpan(offset, SaltSize).ToArray(); offset += SaltSize;
        var nonce = data.AsSpan(offset, NonceSize).ToArray(); offset += NonceSize;
        var tag = data.AsSpan(offset, TagSize).ToArray(); offset += TagSize;
        var cipher = data.AsSpan(offset).ToArray();
        var plain = new byte[cipher.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            await File.WriteAllBytesAsync(destination, plain, ct);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("The backup password is incorrect or the file is damaged.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || Encoding.UTF8.GetByteCount(passphrase) < 12)
            throw new ArgumentException("Use a backup password containing at least 12 characters.", nameof(passphrase));
    }
}

