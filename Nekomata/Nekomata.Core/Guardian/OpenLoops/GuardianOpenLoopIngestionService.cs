using System.Text.RegularExpressions;
using Nekomata.Data.Repositories;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Integrations.MicrosoftGraph.Mail;
using Nekomata.Models.Guardian;

namespace Nekomata.Core.Guardian.OpenLoops;

public sealed partial class GuardianOpenLoopIngestionService(
    GuardianOpenLoopService openLoops,
    IGuardianSignalCheckpointRepository? checkpoints = null,
    Nekomata.Core.Guardian.Context.ProjectCommunicationLoopService? projectCommunications = null)
{
    public async Task IngestMailAsync(IEnumerable<EmailMessage> inbox, IEnumerable<EmailMessage> sent)
    {
        var newSent = await SinceCheckpointAsync("mail-sent", sent, x => x.ReceivedAt);
        foreach (var message in newSent)
        {
            var authored = ExtractAuthoredText(message.BodyPreview);
            if (!IsRequest(authored)) continue;
            var detected = GuardianOpenLoopDetector.Detect(new GuardianOpenLoopSignal
            {
                Kind = GuardianOpenLoopSignalKind.Email,
                EventId = StableEventId("mail-sent", message.Id), OccurredAt = message.ReceivedAt,
                Title = $"Awaiting reply: {message.Subject}", Summary = authored,
                PersonName = string.Join(", ", message.ToRecipients),
                PersonAddress = string.Join(";", message.ToRecipientAddresses),
                IsOutbound = true, ContainsQuestionOrRequest = true,
                EmailId = message.ConversationId
            });
            if (detected is not null) await openLoops.CaptureAsync(detected);
        }
        if (projectCommunications is not null) await projectCommunications.AnalyseSentMailAsync(newSent);
        await AdvanceCheckpointAsync("mail-sent", sent, x => x.ReceivedAt);

        var newInbox = await SinceCheckpointAsync("mail-received", inbox, x => x.ReceivedAt);
        foreach (var message in newInbox.Where(x => !string.IsNullOrWhiteSpace(x.ConversationId)))
            await openLoops.ResolveFromEvidenceAsync(new GuardianLoopEvidence
            {
                EventId = StableEventId("mail-received", message.Id), Kind = "ReplyReceived",
                OccurredAt = message.ReceivedAt, Summary = $"Reply received: {message.Subject}",
                EmailId = message.ConversationId
            });
        await AdvanceCheckpointAsync("mail-received", inbox, x => x.ReceivedAt);
    }

    public async Task IngestTaskCompletionAsync(long taskId, string title)
    {
        await openLoops.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = StableEventId("task-completed", taskId.ToString()), Kind = "TaskCompleted",
            TaskId = taskId, Summary = $"Task completed: {title}"
        });
    }

    public async Task IngestChatAsync(IEnumerable<GraphChatMessage> messages)
    {
        var newMessages = await SinceCheckpointAsync("teams-message", messages, x => x.CreatedAt);
        foreach (var message in newMessages.Where(x => !x.IsFromCurrentUser && RequestsMeeting(x.Body)))
        {
            var detected = GuardianOpenLoopDetector.Detect(new GuardianOpenLoopSignal
            {
                Kind = GuardianOpenLoopSignalKind.Message,
                EventId = StableEventId("teams-message", message.Id), OccurredAt = message.CreatedAt,
                Title = $"Arrange time with {message.Sender}", Summary = message.Body, PersonName = message.Sender,
                RequestsMeetingArrangement = true, TeamsMessageId = message.Id
            });
            if (detected is not null) await openLoops.CaptureAsync(detected);
        }
        await AdvanceCheckpointAsync("teams-message", messages, x => x.CreatedAt);
    }

    public async Task IngestMeetingScheduledAsync(string calendarEventId, string subject)
    {
        await openLoops.ResolveFromEvidenceAsync(new GuardianLoopEvidence
        {
            EventId = StableEventId("meeting-scheduled", calendarEventId), Kind = "MeetingScheduled",
            CalendarEventId = calendarEventId, Summary = $"Meeting scheduled: {subject}"
        });
    }

    public static string ExtractAuthoredText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var match = QuotedContentBoundary().Match(body);
        var authored = (match.Success ? body[..match.Index] : body).Trim();
        if (authored.StartsWith("Microsoft Teams meeting", StringComparison.OrdinalIgnoreCase)) return "";
        return authored;
    }

    public static bool IsRequest(string authoredText)
    {
        if (string.IsNullOrWhiteSpace(authoredText)) return false;
        return DirectRequest().IsMatch(authoredText);
    }

    public static bool RequestsMeeting(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return DirectMeetingRequest().IsMatch(text);
    }

    private async Task<List<T>> SinceCheckpointAsync<T>(string source, IEnumerable<T> signals, Func<T, DateTimeOffset> timestamp)
    {
        var items = signals.ToList();
        if (checkpoints is null) return items;
        var checkpoint = await checkpoints.GetAsync(source);
        return checkpoint is null ? items : items.Where(x => timestamp(x).ToUniversalTime() > checkpoint.Value.ToUniversalTime()).ToList();
    }

    private async Task AdvanceCheckpointAsync<T>(string source, IEnumerable<T> signals, Func<T, DateTimeOffset> timestamp)
    {
        if (checkpoints is null) return;
        var items = signals.ToList();
        if (items.Count > 0) await checkpoints.SetAsync(source, items.Max(timestamp).ToUniversalTime());
    }

    private static Guid StableEventId(string source, string id)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{source}:{id}"));
        return new Guid(bytes[..16]);
    }

    [GeneratedRegex(@"(?im)^\s*(?:_{5,}|-{5,}|From:\s|Sent:\s|On .+ wrote:|Begin forwarded message:|Microsoft Teams meeting\s*$)")]
    private static partial Regex QuotedContentBoundary();

    [GeneratedRegex(@"(?i)\b(?:(?:can|could|would|will)\s+(?:you|someone|anyone)|(?:do|should|shall|may|can)\s+I|please\s+(?:confirm|send|provide|check|review|advise|reply)|let\s+me\s+know|(?:confirm|advise)\s+(?:whether|if|when|the))\b")]
    private static partial Regex DirectRequest();

    [GeneratedRegex(@"(?i)\b(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?(?:arrange|book|schedule|find)|please\s+(?:arrange|book|schedule|find)|(?:arrange|book|schedule|find)\s+(?:a\s+)?(?:meeting|call|time))\b")]
    private static partial Regex DirectMeetingRequest();
}
