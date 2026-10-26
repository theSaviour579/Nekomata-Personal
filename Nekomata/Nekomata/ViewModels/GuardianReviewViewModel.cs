using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Actions;
using Nekomata.Models.AI;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class GuardianReviewViewModel : ObservableObject
{
    private readonly IGuardianApplyService _applyService;
    private readonly Func<Task> _afterApply;

    [ObservableProperty]
    private GuardianTaskActionPlan actionPlan;

    [ObservableProperty]
    private bool isApplying;

    [ObservableProperty]
    private string statusMessage = "Select the changes Guardian should apply.";

    public GuardianReviewViewModel(
        GuardianTaskActionPlan actionPlan,
        IGuardianApplyService applyService,
        Func<Task> afterApply)
    {
        ActionPlan = actionPlan;
        _applyService = applyService;
        _afterApply = afterApply;
    }

    public int SelectedCount => ActionPlan.Actions.Count(action => action.Selected);
    public int TotalCount => ActionPlan.Actions.Count;

    [RelayCommand]
    private async Task ApplyAsync(Window? window)
    {
        if (IsApplying) return;
        var selected = ActionPlan.Actions.Where(action => action.Selected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one action to apply.";
            return;
        }

        IsApplying = true;
        try
        {
            var result = await _applyService.ApplyAsync(BuildResponse(selected));
            await _afterApply();
            StatusMessage = string.Join(" ", result.Messages);
            if (result.Success)
            {
                MessageBox.Show(
                    $"Guardian applied {result.Actions.Count} meeting action{(result.Actions.Count == 1 ? "" : "s")}. Urgent tasks are now in the workspace.",
                    "Meeting actions imported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                window?.Close();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not apply the meeting actions: {ex.Message}";
        }
        finally
        {
            IsApplying = false;
        }
    }

    [RelayCommand]
    private static void Cancel(Window? window) => window?.Close();

    private GuardianActionResponse BuildResponse(IReadOnlyCollection<GuardianTaskAction> actions)
    {
        var response = new GuardianActionResponse
        {
            Message = ActionPlan.Summary,
            ActionType = "MeetingImport",
            Confidence = actions.Count == 0 ? 0 : (int)Math.Round(actions.Average(action => action.Confidence))
        };

        foreach (var action in actions)
        {
            if (action.ActionType.Equals("Add", StringComparison.OrdinalIgnoreCase))
            {
                response.Tasks.Add(new ProposedTask
                {
                    Title = action.Title,
                    Description = action.Description ?? action.Reason ?? "Imported from meeting analysis.",
                    Priority = action.Priority ?? "Normal",
                    EstimatedMinutes = action.EstimatedMinutes ?? 30,
                    DueAt = action.DueAt,
                    ProjectId = action.ProjectId,
                    EstimatedBusinessValue = action.EstimatedBusinessValue ?? 0,
                    RequiresFocus = action.Category?.Contains("project", StringComparison.OrdinalIgnoreCase) == true,
                    Selected = true
                });
                continue;
            }

            var entityId = action.TaskId ?? action.ProjectId;
            if (entityId is null) continue;
            response.Changes.Add(new GuardianChange
            {
                EntityType = action.TaskId is not null ? "Task" : "Project",
                EntityId = entityId.Value,
                Property = InferProperty(action),
                NewValue = InferNewValue(action),
                Reason = action.Reason ?? ActionPlan.Summary,
                Confidence = action.Confidence,
                Selected = true
            });
        }

        return response;
    }

    private static string InferProperty(GuardianTaskAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.ProposedProperty)) return action.ProposedProperty;
        if (action.DueAt is not null) return "DueAt";
        if (!string.IsNullOrWhiteSpace(action.Status)) return "Status";
        if (!string.IsNullOrWhiteSpace(action.Priority)) return "Priority";
        return "Status";
    }

    private static string InferNewValue(GuardianTaskAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.ProposedValue)) return action.ProposedValue;
        if (action.DueAt is DateTime dueAt) return dueAt.ToString("O");
        if (!string.IsNullOrWhiteSpace(action.Status)) return action.Status;
        return action.Priority ?? "Open";
    }
}
