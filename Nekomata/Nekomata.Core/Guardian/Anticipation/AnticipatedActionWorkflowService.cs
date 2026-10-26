using Nekomata.Core.Guardian.Outcomes;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Models.Guardian;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Guardian.Anticipation;

public sealed record AnticipatedActionSendResult(AnticipatedAction Action, bool Sent, string Message);

public sealed class AnticipatedActionWorkflowService(IGraphContextService graph, WorkdayJournalService journal)
{
    public static bool IsApproveCommand(string message)
    {
        var value = Normalise(message);
        return value.Contains("approve") && (value.Contains("teams") || value.Contains("anticipated") ||
            value.Contains("prepared response") || value.Contains("prepared reply"));
    }

    public static bool IsSendCommand(string message)
    {
        var value = Normalise(message);
        return value.Contains("send") && value.Contains("approved") &&
            (value.Contains("teams") || value.Contains("anticipated") || value.Contains("response") || value.Contains("reply"));
    }

    public async Task<IReadOnlyList<AnticipatedAction>> ApprovePreparedAsync(NekomataWorkspace workspace, DateTimeOffset now)
    {
        var approved = workspace.AnticipatedActions.Where(x => x.Status == AnticipatedActionStatus.Prepared &&
            !string.IsNullOrWhiteSpace(x.PreparedContent)).ToList();
        foreach (var action in approved)
        {
            action.Status = AnticipatedActionStatus.Approved;
            action.ApprovedAt = now.ToUniversalTime();
            await journal.RecordAsync(Event(action, "AnticipatedActionApproved", "Approved", now,
                $"Prepared Teams response approved for sending. Nothing has been sent yet. {action.Reason}"));
        }
        return approved;
    }

    public async Task<IReadOnlyList<AnticipatedActionSendResult>> SendApprovedAsync(NekomataWorkspace workspace, DateTimeOffset now)
    {
        var results = new List<AnticipatedActionSendResult>();
        foreach (var action in workspace.AnticipatedActions.Where(x => x.Status == AnticipatedActionStatus.Approved).ToList())
        {
            action.LastSendAttemptAt = now.ToUniversalTime();
            try
            {
                await graph.SendChatMessageAsync(action.TargetConversationId, action.PreparedContent);
                action.Status = AnticipatedActionStatus.Sent;
                action.SentAt = now.ToUniversalTime();
                action.LastSendError = "";
                await journal.RecordAsync(Event(action, "AnticipatedActionSent", "Sent", now,
                    "Microsoft Graph confirmed that the approved Teams response was sent."));
                results.Add(new(action, true, "Sent"));
            }
            catch (Exception ex)
            {
                // Approval remains authoritative; a transport failure must never be represented as sent.
                action.LastSendError = ex.Message;
                await journal.RecordAsync(Event(action, "AnticipatedActionSendFailed", "Approved", now,
                    $"The approved Teams response could not be sent: {ex.Message}"));
                results.Add(new(action, false, $"Approved but could not be sent: {ex.Message}"));
            }
        }
        return results;
    }

    private static WorkdayEvent Event(AnticipatedAction action, string type, string outcome, DateTimeOffset now, string narrative) => new()
    {
        EventId = Guid.NewGuid(), CorrelationId = action.Id, OccurredAt = now, EventType = type,
        Category = "Anticipation", Title = action.ProposedAction, Narrative = narrative,
        ExternalId = $"{action.Id:N}:{type}:{now.UtcTicks}", Outcome = outcome, UserDecision = outcome == "Approved" ? "Approved" : "",
        Significance = 55
    };

    private static string Normalise(string message) => message.Trim().ToLowerInvariant().Replace('’', '\'');
}
