using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public sealed class GuardianAttachmentItem(GuardianFileAttachment attachment)
{
    public GuardianFileAttachment Attachment { get; } = attachment;
    public string FileName => Attachment.FileName;
    public string SizeLabel => Attachment.SizeBytes < 1024 * 1024
        ? $"{Math.Max(1, Attachment.SizeBytes / 1024)} KB"
        : $"{Attachment.SizeBytes / 1024d / 1024d:0.0} MB";
}
