using Nekomata.AI.Models.Actions;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.Relationships;

public static class GuardianRelationshipAnalyzer
{
    public static IReadOnlyList<GuardianRelationshipObservation> Analyse(
        IReadOnlyList<EmailMessage> messages,
        string ownAddress,
        ChaseConversationAdvice advice)
    {
        if (messages.Count == 0 || string.IsNullOrWhiteSpace(ownAddress)) return [];
        var ordered = messages.OrderBy(x => x.ReceivedAt).ToList();
        var conversationId = ordered.Select(x => x.ConversationId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (string.IsNullOrWhiteSpace(conversationId)) return [];

        return ordered
            .Where(x => !Own(x, ownAddress) && !string.IsNullOrWhiteSpace(x.SenderAddress))
            .GroupBy(x => x.SenderAddress.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => AnalyseContact(ordered, group.ToList(), ownAddress, conversationId, advice))
            .ToList();
    }

    private static GuardianRelationshipObservation AnalyseContact(
        IReadOnlyList<EmailMessage> all,
        IReadOnlyList<EmailMessage> contactMessages,
        string ownAddress,
        string conversationId,
        ChaseConversationAdvice advice)
    {
        var contactAddress = contactMessages[0].SenderAddress.Trim().ToLowerInvariant();
        var acknowledgements = new HashSet<string>(advice.AcknowledgementMessageIds, StringComparer.OrdinalIgnoreCase);
        var responses = 0;
        long totalResponseMinutes = 0;
        var chaseResponses = 0;
        EmailMessage? pendingOutgoing = null;

        foreach (var message in all)
        {
            if (Own(message, ownAddress))
            {
                pendingOutgoing = message;
                continue;
            }
            if (!message.SenderAddress.Equals(contactAddress, StringComparison.OrdinalIgnoreCase) || acknowledgements.Contains(message.Id))
                continue;
            if (pendingOutgoing is null) continue;
            var minutes = (long)Math.Round((message.ReceivedAt - pendingOutgoing.ReceivedAt).TotalMinutes);
            if (minutes is >= 0 and <= 43200)
            {
                responses++;
                totalResponseMinutes += minutes;
                if (pendingOutgoing.Id.Equals(advice.LastChaseMessageId, StringComparison.OrdinalIgnoreCase) || IsChase(pendingOutgoing.BodyContent))
                    chaseResponses++;
            }
            pendingOutgoing = null;
        }

        var substantive = contactMessages.Where(x => !acknowledgements.Contains(x.Id)).ToList();
        return new GuardianRelationshipObservation
        {
            ContactAddress=contactAddress,
            ContactName=contactMessages.LastOrDefault(x => !string.IsNullOrWhiteSpace(x.SenderName))?.SenderName ?? contactAddress,
            Channel="Email",
            EvidenceQuality=100,
            ConversationId=conversationId,
            MessageCount=contactMessages.Count,
            SubstantiveReplyCount=substantive.Count,
            AcknowledgementCount=contactMessages.Count-substantive.Count,
            ResponseCount=responses,
            TotalResponseMinutes=totalResponseMinutes,
            ChaseResponseCount=chaseResponses,
            PromiseCount=contactMessages.Count(x => x.Id.Equals(advice.PromiseMessageId, StringComparison.OrdinalIgnoreCase)),
            DetailedMessageCount=substantive.Count(x => WordCount(x.BodyContent) >= 120),
            ConciseMessageCount=substantive.Count(x => WordCount(x.BodyContent) <= 40),
            LastMessageAt=contactMessages.Max(x => x.ReceivedAt),
            ObservedAt=DateTimeOffset.UtcNow
        };
    }

    private static bool Own(EmailMessage message, string ownAddress) =>
        message.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase);

    private static bool IsChase(string text) => new[] { "following up", "follow up", "follow-up", "chasing", "any update" }
        .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
