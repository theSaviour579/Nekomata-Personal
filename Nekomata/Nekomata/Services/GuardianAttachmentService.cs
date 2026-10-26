using System.Security.Cryptography;
using System.IO;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.Services;

public sealed class GuardianAttachmentService
{
    private const long MaximumBytes = 25 * 1024 * 1024;
    private const int MaximumExtractedCharacters = 100_000;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".csv", ".tsv", ".sql", ".json", ".xml", ".md", ".log" };

    public async Task<GuardianFileAttachment> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists) throw new FileNotFoundException("The selected attachment no longer exists.", sourcePath);
        if (source.Length > MaximumBytes) throw new InvalidOperationException("Attachments must be 25 MB or smaller.");

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nekomata Personal", "GuardianAttachments", DateTime.Today.ToString("yyyy-MM"));
        Directory.CreateDirectory(root);
        var storedName = $"{Guid.NewGuid():N}{source.Extension.ToLowerInvariant()}";
        var storedPath = Path.Combine(root, storedName);
        await using (var input = source.OpenRead())
        await using (var output = File.Create(storedPath))
            await input.CopyToAsync(output, cancellationToken);

        string hash;
        await using (var stream = File.OpenRead(storedPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();

        var extracted = "";
        if (TextExtensions.Contains(source.Extension))
        {
            extracted = await File.ReadAllTextAsync(storedPath, cancellationToken);
            if (extracted.Length > MaximumExtractedCharacters)
                extracted = extracted[..MaximumExtractedCharacters] + "\n[Attachment text truncated]";
        }

        return new GuardianFileAttachment
        {
            FileName = source.Name, StoredPath = storedPath, SizeBytes = source.Length,
            Sha256 = hash, ContentType = GetContentType(source.Extension), ExtractedText = extracted
        };
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf", ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv", ".sql" => "text/plain", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        _ when TextExtensions.Contains(extension) => "text/plain", _ => "application/octet-stream"
    };
}
