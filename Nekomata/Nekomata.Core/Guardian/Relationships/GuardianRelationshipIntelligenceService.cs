using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Models.Guardian;
using Nekomata.Core.Guardian.OpenLoops;

namespace Nekomata.Core.Guardian.Relationships;

public sealed class GuardianRelationshipIntelligenceService(IGuardianRelationshipRepository repository)
{
    public async Task ObserveConversationAsync(
        IReadOnlyList<EmailMessage> messages,
        string ownAddress,
        ChaseConversationAdvice advice)
    {
        foreach (var observation in GuardianRelationshipAnalyzer.Analyse(messages, ownAddress, advice))
            await repository.UpsertObservationAsync(observation);
    }

    public Task<GuardianRelationshipProfile?> GetProfileAsync(string contactAddress) =>
        repository.GetProfileAsync(contactAddress);

    public async Task<GuardianRelationshipObservationSummary> ObserveRecentMailAsync(
        IReadOnlyList<EmailMessage> inbox,
        IReadOnlyList<EmailMessage> sent,
        string ownAddress,
        DateTimeOffset now)
    {
        var messages = inbox.Concat(sent).DistinctBy(x => x.Id).ToList();
        var conversations = messages.Where(x => !string.IsNullOrWhiteSpace(x.ConversationId))
            .GroupBy(x => x.ConversationId, StringComparer.OrdinalIgnoreCase).ToList();
        var observed = 0;
        foreach (var conversation in conversations)
        {
            var group = conversation.OrderBy(x => x.ReceivedAt).ToList();
            if (!group.Any(x => x.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase)) ||
                !group.Any(x => !x.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase))) continue;
            var advice = new ChaseConversationAdvice
            {
                AcknowledgementMessageIds = group.Where(x => LooksLikeAcknowledgement(x.BodyContent)).Select(x => x.Id).ToList(),
                LastChaseMessageId = group.LastOrDefault(x => x.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase) &&
                    LooksLikeChase(x.BodyContent))?.Id ?? ""
            };
            foreach (var observation in GuardianRelationshipAnalyzer.Analyse(group, ownAddress, advice))
            {
                if (IsAutomatedAddress(observation.ContactAddress)) continue;
                observation.EvidenceQuality = 50;
                await repository.UpsertObservationAsync(observation);
                observed++;
            }
        }

        var delays = new List<GuardianRelationshipDelayInsight>();
        foreach (var conversation in conversations)
        {
            var group = conversation.OrderBy(x => x.ReceivedAt).ToList();
            var latest = group.LastOrDefault();
            if (latest is null || !latest.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase)) continue;
            if (!RequestsResponse(latest.BodyContent)) continue;
            var contact = group.LastOrDefault(x => !x.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase));
            if (contact is null || IsAutomatedAddress(contact.SenderAddress)) continue;
            var profile = await repository.GetProfileAsync(contact.SenderAddress);
            if (profile is not { HasEnoughEvidence: true, IsExcluded: false }) continue;
            var due = GuardianChasePolicy.AddWorkingDays(latest.ReceivedAt.ToLocalTime(), profile.RecommendedWorkingDayWait);
            if (now <= due) continue;
            delays.Add(new(contact.SenderAddress, contact.SenderName, conversation.Key, latest.Subject,
                latest.ReceivedAt, due, profile.Confidence));
        }
        return new(observed, delays.OrderBy(x => x.ExpectedBy).ToList());
    }

    public async Task ObserveTeamsAsync(IReadOnlyList<GraphChatMessage> messages)
    {
        foreach (var chat in messages.Where(x => !string.IsNullOrWhiteSpace(x.ChatId))
                     .GroupBy(x => x.ChatId, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = chat.OrderBy(x => x.CreatedAt).ToList();
            foreach (var contact in ordered.Where(x => !x.IsFromCurrentUser && !string.IsNullOrWhiteSpace(x.Sender))
                         .GroupBy(x => x.Sender.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                GraphChatMessage? pending = null;
                var responses = 0;
                long minutes = 0;
                var chaseResponses = 0;
                foreach (var message in ordered)
                {
                    if (message.IsFromCurrentUser) { pending = message; continue; }
                    if (!message.Sender.Equals(contact.Key, StringComparison.OrdinalIgnoreCase) || LooksLikeAcknowledgement(message.Body)) continue;
                    if (pending is null) continue;
                    var elapsed = (long)Math.Round((message.CreatedAt - pending.CreatedAt).TotalMinutes);
                    if (elapsed is >= 0 and <= 43200)
                    {
                        responses++; minutes += elapsed;
                        if (LooksLikeChase(pending.Body)) chaseResponses++;
                    }
                    pending = null;
                }
                var substantive = contact.Where(x => !LooksLikeAcknowledgement(x.Body)).ToList();
                await repository.UpsertObservationAsync(new GuardianRelationshipObservation
                {
                    ContactAddress="teams:"+NormaliseIdentity(contact.Key),ContactName=contact.Key,Channel="Teams",
                    ConversationId="teams:"+chat.Key,EvidenceQuality=45,MessageCount=contact.Count(),
                    SubstantiveReplyCount=substantive.Count,AcknowledgementCount=contact.Count()-substantive.Count,
                    ResponseCount=responses,TotalResponseMinutes=minutes,ChaseResponseCount=chaseResponses,
                    DetailedMessageCount=substantive.Count(x=>WordCount(x.Body)>=120),
                    ConciseMessageCount=substantive.Count(x=>WordCount(x.Body)<=40),
                    LastMessageAt=contact.Max(x=>x.CreatedAt),ObservedAt=DateTimeOffset.UtcNow
                });
            }
        }
    }

    public Task<IReadOnlyList<GuardianRelationshipProfile>> GetProfilesAsync(int count = 100) =>
        repository.GetProfilesAsync(count);

    public Task SetExcludedAsync(GuardianRelationshipProfile profile, bool excluded, string note = "") =>
        repository.SetExcludedAsync(profile.ContactAddress, profile.ContactName, excluded, note);

    private static bool LooksLikeAcknowledgement(string text)
    {
        var value = text.Trim();
        if (value.Contains('?') || value.Length > 500) return false;
        return new[] { "automatic reply", "automated response", "we have received", "thank you for contacting", "ticket has been logged", "out of office" }
            .Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeChase(string text) =>
        new[] { "following up", "follow up", "follow-up", "chasing", "any update" }
            .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsAutomatedAddress(string address) =>
        new[] { "no-reply", "noreply", "mailer-daemon", "postmaster", "notifications@" }
            .Any(marker => address.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool RequestsResponse(string text) => text.Contains('?') ||
        new[] { "please confirm", "could you", "can you", "let me know", "provide an update", "any update", "your thoughts" }
            .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string NormaliseIdentity(string value) =>
        string.Join('-', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
