using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;
using Nekomata.Models.Guardian;
using Nekomata.UI.Windows;
using System.Text.Json;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void RemoveGuardianProposalItem(object? item)
    {
        if (item is ProposedTask { IsRemoved: true } or GuardianChange { IsRemoved: true })
        {
            RestoreGuardianProposalItem(item);
            return;
        }
        var title = ProposalItemTitle(item);
        if (string.IsNullOrWhiteSpace(title)) return;
        var dialog = new ProposalFeedbackWindow(title, false) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        switch (item)
        {
            case ProposedTask task:
                task.OriginalTitle = string.IsNullOrWhiteSpace(task.OriginalTitle) ? task.Title : task.OriginalTitle;
                task.ReviewDisposition = "Removed"; task.FeedbackReason = dialog.FeedbackReason; task.Selected = false;
                break;
            case GuardianChange change:
                CaptureOriginal(change, title);
                change.ReviewDisposition = "Removed"; change.FeedbackReason = dialog.FeedbackReason; change.Selected = false;
                break;
        }
    }

    [RelayCommand]
    private void ReplaceGuardianProposalItem(object? item)
    {
        var title = ProposalItemTitle(item);
        if (string.IsNullOrWhiteSpace(title)) return;
        var calendar = item is GuardianChange { IsCalendarChange: true };
        var dialog = new ProposalFeedbackWindow(title, true, calendar ? "CALENDAR REPLACEMENT OBJECTIVE" : "REPLACEMENT TITLE OR VALUE")
        { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        switch (item)
        {
            case ProposedTask task:
                task.OriginalTitle = string.IsNullOrWhiteSpace(task.OriginalTitle) ? task.Title : task.OriginalTitle;
                task.Title = dialog.Replacement;
                task.ReplacementInstruction = dialog.Replacement;
                task.ReviewDisposition = "Replaced"; task.FeedbackReason = dialog.FeedbackReason; task.Selected = true;
                break;
            case GuardianChange change when change.IsCalendarChange:
                CaptureOriginal(change, title);
                var candidate = FindReplacementCandidate(dialog.Replacement);
                if (candidate is null)
                {
                    MessageBox.Show("Guardian could not find another unscheduled eligible objective. Enter a matching objective title or refresh the workspace.", "Replace suggestion");
                    return;
                }
                var parts = change.NewValue.Split('|', 3, StringSplitOptions.TrimEntries);
                if (parts.Length < 3) return;
                change.NewValue = $"{parts[0]}|{parts[1]}|{candidate.Title}";
                change.EntityId = candidate.TaskId ?? 0;
                change.ReplacementInstruction = candidate.Title;
                change.ReviewDisposition = "Replaced"; change.FeedbackReason = dialog.FeedbackReason; change.Selected = true;
                break;
            case GuardianChange change:
                CaptureOriginal(change, title);
                change.NewValue = dialog.Replacement;
                change.ReplacementInstruction = dialog.Replacement;
                change.ReviewDisposition = "Replaced"; change.FeedbackReason = dialog.FeedbackReason; change.Selected = true;
                break;
        }
    }

    [RelayCommand]
    private void RestoreGuardianProposalItem(object? item)
    {
        switch (item)
        {
            case ProposedTask task:
                if (!string.IsNullOrWhiteSpace(task.OriginalTitle)) task.Title = task.OriginalTitle;
                task.ReviewDisposition = task.FeedbackReason = task.ReplacementInstruction = "";
                task.Selected = true; task.FeedbackRecorded = false;
                break;
            case GuardianChange change:
                if (!string.IsNullOrWhiteSpace(change.OriginalValue)) change.NewValue = change.OriginalValue;
                if (change.OriginalEntityId != 0) change.EntityId = change.OriginalEntityId;
                change.ReviewDisposition = change.FeedbackReason = change.ReplacementInstruction = "";
                change.Selected = true; change.FeedbackRecorded = false;
                break;
        }
    }

    private Nekomata.Models.Missions.MissionCandidate? FindReplacementCandidate(string requested)
    {
        var proposedTitles = PendingGuardianAction?.Changes.Where(x => x.Selected && x.IsCalendarChange)
            .Select(x => x.CalendarTitle).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var rejectedTitles = PendingGuardianAction?.Changes.Where(x => x.HasReviewFeedback)
            .Select(x => x.OriginalTitle).Concat(PendingGuardianAction.Tasks.Where(x => x.HasReviewFeedback).Select(x => x.OriginalTitle))
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var candidates = Workspace.RankedMissionCandidates.Where(x => x.IsActionable && x.TaskId is > 0 && x.EstimatedMinutes > 0 &&
            !proposedTitles.Contains(x.Title) && !rejectedTitles.Contains(x.Title));
        if (!string.IsNullOrWhiteSpace(requested))
            return candidates.OrderByDescending(x => x.Title.Equals(requested, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.Title.Contains(requested, StringComparison.OrdinalIgnoreCase) || requested.Contains(x.Title, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.Rank <= 0 ? int.MaxValue : x.Rank).FirstOrDefault();
        return candidates.OrderBy(x => x.Rank <= 0 ? int.MaxValue : x.Rank).ThenByDescending(x => x.Score).FirstOrDefault();
    }

    private async Task<int> PersistProposalFeedbackAsync()
    {
        if (PendingGuardianAction is null) return 0;
        var memories = new List<GuardianMemory>();
        foreach (var task in PendingGuardianAction.Tasks.Where(x => x.HasReviewFeedback && !x.FeedbackRecorded))
        {
            memories.Add(BuildFeedbackMemory("Task", task.ReviewDisposition, task.OriginalTitle, task.Title,
                task.FeedbackReason, task.ReplacementInstruction, task.ProjectId ?? PendingGuardianProject?.Id));
            task.FeedbackRecorded = true;
        }
        foreach (var change in PendingGuardianAction.Changes.Where(x => x.HasReviewFeedback && !x.FeedbackRecorded))
        {
            memories.Add(BuildFeedbackMemory(change.EntityType, change.ReviewDisposition, change.OriginalTitle,
                change.IsCalendarChange ? change.CalendarTitle : change.NewValue, change.FeedbackReason,
                change.ReplacementInstruction, PendingGuardianProject?.Id));
            change.FeedbackRecorded = true;
        }
        var repository = _services.GetRequiredService<IGuardianMemoryRepository>();
        foreach (var memory in memories) await repository.AddAsync(memory);
        return memories.Count;
    }

    private static GuardianMemory BuildFeedbackMemory(string itemType, string disposition, string originalTitle,
        string currentTitle, string reason, string replacement, long? projectId) => new()
    {
        Category = "ProposalFeedback", Importance = 88, Source = "GuardianReview", ProjectId = projectId,
        Summary = $"{disposition} proposal: {originalTitle}",
        Detail = disposition.Equals("Replaced", StringComparison.OrdinalIgnoreCase)
            ? $"Replace '{originalTitle}' with '{currentTitle}'. Reason: {reason}"
            : $"Do not propose '{originalTitle}' in a similar plan without new evidence. Reason: {reason}",
        Metadata = JsonSerializer.Serialize(new { itemType, disposition, originalTitle, replacement, reason })
    };

    private static string ProposalItemTitle(object? item) => item switch
    {
        ProposedTask task => task.Title,
        GuardianChange change when change.IsCalendarChange => change.CalendarTitle,
        GuardianChange change => $"{change.EntityType} {change.Property}: {change.NewValue}",
        _ => ""
    };

    private static void CaptureOriginal(GuardianChange change, string title)
    {
        if (string.IsNullOrWhiteSpace(change.OriginalValue)) change.OriginalValue = change.NewValue;
        if (string.IsNullOrWhiteSpace(change.OriginalTitle)) change.OriginalTitle = title;
        if (change.OriginalEntityId == 0) change.OriginalEntityId = change.EntityId;
    }
}
