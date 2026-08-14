namespace Nekomata.Integrations.MicrosoftGraph.Mail;

public sealed class EmailMessage
{
    public string Id { get; init; } = "";
    public string Subject { get; init; } = "(No subject)";
    public string SenderName { get; init; } = "Unknown sender";
    public string SenderAddress { get; init; } = "";
    public DateTimeOffset ReceivedAt { get; init; }
    public string Importance { get; init; } = "normal";
    public bool IsRead { get; init; }
    public bool HasAttachments { get; init; }
    public string BodyPreview { get; init; } = "";
    public string BodyContent { get; init; } = "";
    public string WebLink { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public IReadOnlyList<string> CcRecipients { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
}

public sealed class EmailDraftResult
{
    public string Id { get; init; } = "";
    public string WebLink { get; init; } = "";
}