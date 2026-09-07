namespace Nekomata.Integrations.MicrosoftGraph.Mail;

public interface IEmailService
{
    Task<string> GetMailboxAddressAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Mailbox identity is unavailable.");
    Task<IReadOnlyList<EmailMessage>> SearchInboxAndSentAsync(string query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Combined email search is unavailable.");
    Task<IReadOnlyList<EmailMessage>> GetConversationMessagesAsync(string conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Conversation lookup is unavailable.");
    Task<EmailMessage?> GetLatestConversationMessageAsync(string conversationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Conversation lookup is unavailable.");
    Task<IReadOnlyList<EmailMessage>> SearchSentMessagesAsync(string query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Sent email search is unavailable.");
    Task<IReadOnlyList<EmailMessage>> GetUnreadInboxAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailMessage>> GetRecentInboxAsync(
        int maximum = 100,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmailMessage>>([]);

    Task<IReadOnlyList<EmailMessage>> SearchMessagesAsync(
        string query,
        int maximum = 20,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmailMessage>>([]);

    Task<string> GetMessageContentAsync(
        string messageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRecentSentMessageBodiesAsync(
        int maximum = 5,
        CancellationToken cancellationToken = default);

    Task<EmailDraftResult> CreateReplyDraftAsync(
        string messageId,
        string body,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmailMessage>> GetRecentSentMessagesAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EmailMessage>>([]);

    Task UpdateDraftAsync(
        string draftId,
        string body,
        CancellationToken cancellationToken = default);

    Task SendDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);
    Task SendMessageAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Sending a new message is not supported by this email service.");
    Task ApplyCategoryAsync(
        string messageId,
        IReadOnlyCollection<string> existingCategories,
        string category,
        CancellationToken cancellationToken = default);
}
