namespace Nekomata.Integrations.MicrosoftGraph.Mail;

public interface IEmailService
{
    Task<IReadOnlyList<EmailMessage>> GetUnreadInboxAsync(
        int maximum = 30,
        CancellationToken cancellationToken = default);

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

    Task UpdateDraftAsync(
        string draftId,
        string body,
        CancellationToken cancellationToken = default);

    Task SendDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default);
    Task ApplyCategoryAsync(
        string messageId,
        IReadOnlyCollection<string> existingCategories,
        string category,
        CancellationToken cancellationToken = default);
}