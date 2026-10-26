using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Guardian.Context;
using Nekomata.Core.Guardian.Authority;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    private async Task RefreshProjectCommunicationAttentionAsync()
    {
        await _services.GetRequiredService<ProjectCommunicationLoopService>().SanitizeOpenLoopsAsync();
        var open = await _services.GetRequiredService<IGuardianOpenLoopRepository>().GetOpenAsync();
        await _services.GetRequiredService<GuardianDecisionOutcomeService>().ObserveProjectOutcomesAsync(open);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var loop in open.Where(ProjectCommunicationLoopService.IsProjectCommunication))
        {
            var project = Workspace.Projects.FirstOrDefault(x => x.Id == loop.ProjectId);
            if (project is null) continue;
            if (loop.Type == OpenLoopType.AwaitingMe && loop.DueAt <= DateTimeOffset.Now)
            {
                var key = "project-response:" + loop.Id.ToString("N");
                activeKeys.Add(key);
                RaiseAttention(key, "PROJECT UPDATE", "Info", loop.Title,
                    $"No later response has been evidenced for this project update.\n\n{loop.Detail}",
                    "open_email", loop.Id.ToString());
            }
            else if (loop.Type == OpenLoopType.AwaitingThem &&
                     !project.Status.Equals("On Hold", StringComparison.OrdinalIgnoreCase) &&
                     !project.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                var authority = await _services.GetRequiredService<GuardianActionAuthorityEngine>().EvaluateAndRecordAsync(new()
                {
                    ActionType = "ChangeProjectStatus", Title = $"Put {project.Name} on hold",
                    Reason = "The next evidenced action belongs to an external party.", Confidence = 90,
                    IsDeterministic = true, Risk = GuardianActionRisk.Medium,
                    Reversibility = GuardianActionReversibility.Reversible,
                    Impact = GuardianActionImpact.Internal, ProjectId = project.Id, SourceEventId = loop.Id,
                    UndoDescription = "Restore the project's previous active status.", Evidence = [loop.Detail]
                });
                var key = "project-hold:" + loop.Id.ToString("N");
                activeKeys.Add(key);
                RaiseAttention(key, "PROJECT STATE", "Info", $"Consider putting {project.Name} on hold",
                    $"Guardian found explicit evidence that the next action is external. Review before changing the project.\n\n{loop.Detail}",
                    "review_project_hold", project.Id.ToString(), decisionOutcomeId: authority.Id);
            }
        }

        foreach (var item in AttentionItems.Where(x => x.IsActive &&
                     (x.Key.StartsWith("project-response:", StringComparison.OrdinalIgnoreCase) ||
                      x.Key.StartsWith("project-hold:", StringComparison.OrdinalIgnoreCase)) &&
                     !activeKeys.Contains(x.Key)).ToList())
            ResolveAttention(item.Key);
    }

    private async Task ReviewProjectHoldAsync(GuardianAttentionItem item)
    {
        if (!long.TryParse(item.ContextId, out var projectId)) return;
        var project = await _services.GetRequiredService<IProjectRepository>().GetByIdAsync(projectId);
        if (project is null) return;
        var decision = MessageBox.Show(
            $"Guardian found evidence that '{project.Name}' is waiting on an external party.\n\n" +
            "Put the project On Hold?\n\nYes: apply On Hold\nNo: reject this suggestion and teach Guardian not to repeat it\nCancel: leave it for later",
            "Review project state", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (decision == MessageBoxResult.Cancel) return;
        if (decision == MessageBoxResult.Yes)
        {
            project.Status = "On Hold";
            project.NextAction = "Await external response; review the linked communication loop.";
            await _services.GetRequiredService<IProjectRepository>().SaveAsync(project);
            var workspaceProject = Workspace.Projects.FirstOrDefault(x => x.Id == project.Id);
            if (workspaceProject is not null)
            {
                workspaceProject.Status = project.Status;
                workspaceProject.NextAction = project.NextAction;
            }
            await RecordWorkdayEventAsync(new()
            {
                EventType = "ProjectStateChanged", Category = "Project", Title = $"{project.Name} put on hold",
                Narrative = "User accepted Guardian's evidence-based external dependency recommendation.",
                ProjectId = project.Id, Outcome = "On Hold", UserDecision = "Accepted", Significance = 60
            });
            if (item.DecisionOutcomeId is { } acceptedId)
                await _services.GetRequiredService<GuardianDecisionOutcomeService>().RecordDecisionAsync(
                    acceptedId, GuardianDecisionDisposition.Accepted, "User approved the suggested project-state change.");
        }
        else
        {
            await RecordWorkdayEventAsync(new()
            {
                EventType = "ProjectStateSuggestionRejected", Category = "Project", Title = $"Kept {project.Name} active",
                Narrative = "User rejected Guardian's external-dependency hold recommendation. This is retained as correction evidence.",
                ProjectId = project.Id, Outcome = project.Status, UserDecision = "Rejected", Significance = 50
            });
            if (item.DecisionOutcomeId is { } rejectedId)
                await _services.GetRequiredService<GuardianDecisionOutcomeService>().RecordDecisionAsync(
                    rejectedId, GuardianDecisionDisposition.Rejected, "User rejected the suggested project-state change.");
        }
        ResolveAttention(item.Key);
    }
}
