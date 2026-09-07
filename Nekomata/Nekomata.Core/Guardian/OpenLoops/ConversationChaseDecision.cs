using Nekomata.AI.Models.Actions;
using Nekomata.Integrations.MicrosoftGraph.Mail;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed record ConversationChaseDecision(string Action, string Evidence, DateTimeOffset? WaitUntil, string Draft)
{
    public static ConversationChaseDecision Evaluate(IReadOnlyList<EmailMessage> messages, string ownAddress,
        ChaseConversationAdvice advice, DateTimeOffset now)
    {
        var latest = messages.OrderByDescending(x => x.ReceivedAt).First();
        bool Own(EmailMessage x) => x.SenderAddress.Equals(ownAddress, StringComparison.OrdinalIgnoreCase);
        bool Acknowledgement(EmailMessage x) => !Own(x) && !x.BodyContent.Contains('?') &&
            advice.AcknowledgementMessageIds.Contains(x.Id) && x.Id != advice.PromiseMessageId &&
            !new[] { "please provide", "please send", "please confirm", "we have fixed", "we resolved" }
                .Any(marker => x.BodyContent.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var effective = messages.Where(x => !Acknowledgement(x)).OrderByDescending(x => x.ReceivedAt).FirstOrDefault();
        var chase = messages.FirstOrDefault(x => x.Id == advice.LastChaseMessageId && Own(x) &&
            advice.ChaseQuote.Length >= 12 && x.BodyContent.Contains(advice.ChaseQuote, StringComparison.OrdinalIgnoreCase) &&
            new[] { "following up", "follow up", "follow-up", "chasing", "any update", "an update on", "heard back" }
                .Any(marker => advice.ChaseQuote.Contains(marker, StringComparison.OrdinalIgnoreCase)) &&
            messages.Any(earlier => Own(earlier) && earlier.ReceivedAt < x.ReceivedAt));
        var evidence = chase == null ? "No verified chase identified in the retrieved messages. "
            : $"Last evidenced chase: {chase.ReceivedAt.LocalDateTime:ddd dd MMM HH:mm}. ";
        evidence += $"Latest message from {latest.SenderAddress} at {latest.ReceivedAt.LocalDateTime:ddd dd MMM HH:mm}. ";
        if (Acknowledgement(latest)) evidence += "The latest message is an acknowledgement only; it does not reset the chase clock. ";
        if (effective == null)
            return new("Review", evidence + "No substantive message or original outgoing request was found.", null, "");
        // A later automatic receipt does not cancel an evidenced promise. A substantive reply does.
        if (!Own(effective) && effective.Id == advice.PromiseMessageId && advice.PromiseQuote.Length >= 12 &&
            effective.BodyContent.Contains(advice.PromiseQuote, StringComparison.OrdinalIgnoreCase))
        {
            if (effective.BodyContent.Contains('?'))
                return new("Respond", evidence + "The message includes a question alongside its commitment. Review whether your response is needed first.", null, advice.SuggestedReply);
            var date = CommitmentDate.FromQuote(advice.PromiseQuote, effective.ReceivedAt.LocalDateTime);
            if (date == null)
                return new("Review", evidence + $"Commitment: “{advice.PromiseQuote}”. What date should I follow up? Use Waiting until to confirm; timing is unclear or conditional.", null, "");
            var until = CommitmentDate.FollowUpAt(date.Value);
            var context = $"Commitment from {effective.SenderAddress} on {effective.ReceivedAt.LocalDateTime:ddd dd MMM}: “{advice.PromiseQuote}”. Due {date:ddd dd MMM yyyy}; follow-up {until.LocalDateTime:ddd dd MMM HH:mm}. ";
            return until > now
                ? new("Wait", evidence + context, until, "")
                : new("Chase", evidence + context + "The promised date has passed with no later substantive reply in the retrieved conversation.", null,
                    $"Hi,\n\nFollowing up on ‘{effective.Subject}’. In your message of {effective.ReceivedAt.LocalDateTime:dd MMM}, you mentioned: “{advice.PromiseQuote}”.\n\nCould you confirm the current position and, if this is still outstanding, the revised expected date?\n\nThanks,");
        }
        if (!Own(effective))
        {
            if (advice.Summary.Contains("no substantive", StringComparison.OrdinalIgnoreCase) ||
                advice.Summary.Contains("only an automatic", StringComparison.OrdinalIgnoreCase))
                return new("Review", evidence + "The analysis describes an acknowledgement but did not consistently classify the message. Check the conversation before deciding to chase or respond.", null, "");
            return new("Respond", evidence + "A reply has arrived; review and respond instead of sending a blind chase. " + advice.Summary, null, advice.SuggestedReply);
        }
        // Chase wording is deliberately question-based: historical email cannot
        // establish that the user has retested or that a fault still occurs today.
        var draft = $"Hi,\n\nCould you please provide an update on ‘{effective.Subject}’, including any outstanding questions and the expected next steps?\n\nThanks,";
        var due = effective.ReceivedAt.ToLocalTime();
        for (var days = 0; days < 2;)
        {
            due = due.AddDays(1);
            if (due.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        }
        return due > now
            ? new("Wait", evidence + $"Allow time for a reply; next check {due:ddd dd MMM HH:mm}.", due, draft)
            : new("Chase", evidence + "No later substantive reply in this conversation. " + advice.Summary, null, draft);
    }
}
