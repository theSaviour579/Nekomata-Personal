using Nekomata.Core.Guardian.OpenLoops;
using Nekomata.Integrations.MicrosoftGraph.Context;
using Nekomata.Models.Integrations;
using Nekomata.Models.Workspace;
using Nekomata.Models.Guardian;
using Nekomata.Data.Repositories;
using System.Security.Cryptography;
using System.Text;
using Nekomata.Core.Guardian.Relationships;

namespace Nekomata.Core.Integrations;

public sealed class MicrosoftGraphContextDataSource(
    IGraphContextService graph,
    GuardianOpenLoopIngestionService openLoops,
    IContextSignalRepository signals,
    GuardianRelationshipIntelligenceService relationships) : IWorkspaceDataSource
{
    public string Name => "Microsoft 365 Context";

    public async Task<WorkspaceDataSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new WorkspaceDataSnapshot { SourceName = Name, RetrievedAt = DateTime.Now };
        var warnings = new List<string>();

        var tasks = await TryAsync(() => graph.GetOpenTasksAsync(cancellationToken), "Tasks.Read", warnings);
        foreach (var task in tasks)
        {
            await signals.UpsertAsync(Signal("Microsoft To Do",ContextSignalKind.AssignedWork,$"{task.ListId}:{task.Id}",task.Title,"",task.Status,
                task.DueAt,task.LastModifiedAt??DateTimeOffset.UtcNow,""));
            snapshot.IntegrationMissions.Add(new IntegrationMission
            {
                SourceType = "Microsoft To Do", SourceRecordId = $"{task.ListId}:{task.Id}",
                Title = task.Title, Status = task.Status, DueAt = task.DueAt?.LocalDateTime,
                LastUpdatedAt = task.LastModifiedAt?.LocalDateTime, IsActionable = true,
                EstimatedMinutes = 30
            });
        }

        var planner=await TryAsync(()=>graph.GetPlannerTasksAsync(cancellationToken),"Tasks.Read (Planner)",warnings);
        foreach(var task in planner)
        {
            await signals.UpsertAsync(Signal("Microsoft Planner",ContextSignalKind.AssignedWork,task.Id,task.Title,
                $"Plan {task.PlanId}; bucket {task.BucketId}; assignees {string.Join(",",task.AssigneeIds)}",
                task.PercentComplete==0?"Not started":"In progress",task.DueAt,task.LastModifiedAt??DateTimeOffset.UtcNow,
                task.AssigneeIds.Count>1?"Shared assignment":"Assigned"));
            snapshot.IntegrationMissions.Add(new IntegrationMission
            {
                SourceType="Microsoft Planner",SourceRecordId=task.Id,Title=task.Title,
                Description=$"Shared Planner work · plan {task.PlanId}",Status=task.PercentComplete==0?"Not started":"In progress",
                DueAt=task.DueAt?.LocalDateTime,LastUpdatedAt=task.LastModifiedAt?.LocalDateTime,IsActionable=true,EstimatedMinutes=30
            });
        }

        // One bounded request per recent chat: enough surrounding history for context without a broad Graph history crawl.
        var messages = await TryAsync(() => graph.GetRecentChatMessagesAsync(20,25,30,cancellationToken), "Chat.Read", warnings);
        foreach(var message in messages)
            await signals.UpsertAsync(Signal("Microsoft Teams",ContextSignalKind.Conversation,message.Id,
                string.IsNullOrWhiteSpace(message.Body)?"Teams conversation":message.Body,message.Body,
                message.IsFromCurrentUser?"Outbound":"Inbound",null,message.CreatedAt,message.Sender,message.ChatId));
        await openLoops.IngestChatAsync(messages);
        try { await relationships.ObserveTeamsAsync(messages); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Teams relationship observation failed: {ex}"); }

        var presence = await TryAsync(() => graph.GetPresenceAsync(cancellationToken), "Presence.Read", warnings);
        if (presence is not null)
            snapshot.Notifications.Add($"Microsoft presence: {presence.Availability} · {presence.Activity}");

        snapshot.Health = new IntegrationHealth
        {
            Connected = warnings.Count < 3,
            LastSuccessfulSync = warnings.Count < 3 ? DateTime.Now : default,
            RecordsLoaded = tasks.Count + planner.Count + messages.Count,
            Status = warnings.Count == 0 ? $"Connected · {tasks.Count} To Do · {planner.Count} Planner · {messages.Count} chat signals" : "Partially connected",
            Error = string.Join(" ", warnings)
        };
        return snapshot;
    }

    private static ContextSignal Signal(string source,ContextSignalKind kind,string externalId,string title,string body,string status,
        DateTimeOffset? dueAt,DateTimeOffset observedAt,string owner,string conversationId="")=>new()
    {
        Source=source,Kind=kind,ExternalId=externalId,ConversationId=conversationId,Title=title,Body=body,
        Owner=owner,Person=owner,Status=status,DueAt=dueAt,ObservedAt=observedAt,
        SourceEventId=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"context-signal:{source}:{externalId}"))[..16])
    };

    private static async Task<IReadOnlyList<T>> TryAsync<T>(Func<Task<IReadOnlyList<T>>> action, string permission, List<string> warnings)
    {
        try { return await action(); }
        catch (Exception ex) { warnings.Add($"{permission} unavailable: {ex.Message}"); return []; }
    }

    private static async Task<T?> TryAsync<T>(Func<Task<T>> action, string permission, List<string> warnings) where T : class
    {
        try { return await action(); }
        catch (Exception ex) { warnings.Add($"{permission} unavailable: {ex.Message}"); return null; }
    }
}
