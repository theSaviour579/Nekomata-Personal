namespace Nekomata.Models.Guardian;

/// <summary>A file attached to a Guardian conversation, copied into the local workspace with any readable text extracted.</summary>
public sealed class GuardianFileAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string StoredPath { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string ExtractedText { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
