using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nekomata.AI.Models.Meetings;
using Nekomata.Core.Meetings;
using Nekomata.Models.AI;
using Nekomata.Models.Workspace;
using Nekomata.UI.Views;
using System.Windows;

namespace Nekomata.UI.ViewModels;

public partial class GuardianTaskPlannerViewModel
    : ObservableObject
{
    private readonly IMeetingAnalysisService
        _meetingAnalysisService;

    private readonly NekomataWorkspace
        _workspace;

    // ============================================================
    // STATE
    // ============================================================

    [ObservableProperty]
    private string instruction = "";

    [ObservableProperty]
    private GuardianTaskActionPlan?
        actionPlan;

    [ObservableProperty]
    private bool isAnalysing;

    [ObservableProperty]
    private string statusMessage =
        "Paste meeting notes or describe what changed.";

    // ============================================================
    // DERIVED STATE
    // ============================================================

    public bool HasPlan =>
        ActionPlan is not null;

    public bool HasActions =>
        ActionPlan?.Actions.Count > 0;

    public bool HasQuestions =>
        ActionPlan?.Questions.Count > 0;

    public bool CanAnalyse =>
        !IsAnalysing &&
        !string.IsNullOrWhiteSpace(
            Instruction);

    public bool CanContinue =>
        ActionPlan is not null &&
        ActionPlan.Actions.Count > 0;

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public GuardianTaskPlannerViewModel(
        IMeetingAnalysisService meetingAnalysisService,
        NekomataWorkspace workspace)
    {
        _meetingAnalysisService =
            meetingAnalysisService;

        _workspace =
            workspace;
    }

    // ============================================================
    // PROPERTY CHANGE HANDLERS
    // ============================================================

    partial void OnInstructionChanged(
        string value)
    {
        AnalyseCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAnalysingChanged(
        bool value)
    {
        AnalyseCommand.NotifyCanExecuteChanged();
    }

    partial void OnActionPlanChanged(
        GuardianTaskActionPlan? value)
    {
        OnPropertyChanged(
            nameof(HasPlan));

        OnPropertyChanged(
            nameof(HasActions));

        OnPropertyChanged(
            nameof(HasQuestions));

        OnPropertyChanged(
            nameof(CanContinue));

        ContinueCommand.NotifyCanExecuteChanged();
    }

    // ============================================================
    // ANALYSE MEETING
    // ============================================================

    [RelayCommand(
        CanExecute = nameof(CanAnalyse))]
    private async Task AnalyseAsync()
    {
        try
        {
            IsAnalysing =
                true;

            StatusMessage =
                "Guardian is analysing the meeting notes...";

            ActionPlan =
                null;

            var meetingAnalysis =
                await _meetingAnalysisService
                    .AnalyseAsync(
                        Instruction,
                        _workspace);

            ActionPlan =
                ConvertToTaskActionPlan(
                    meetingAnalysis);

            StatusMessage =
                BuildStatusMessage(
                    ActionPlan);
        }
        catch (Exception ex)
        {
            ActionPlan =
                null;

            StatusMessage =
                "Guardian could not analyse the meeting notes.";

            MessageBox.Show(
                ex.Message,
                "Guardian Meeting Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsAnalysing =
                false;
        }
    }

    // ============================================================
    // CLEAR
    // ============================================================

    [RelayCommand]
    private void Clear()
    {
        Instruction =
            "";

        ActionPlan =
            null;

        StatusMessage =
            "Paste meeting notes or describe what changed.";
    }

    // ============================================================
    // CONTINUE TO REVIEW
    // ============================================================

    [RelayCommand(
        CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        if (ActionPlan is null)
            return;

        var viewModel =
            new GuardianReviewViewModel(
                ActionPlan);

        var window =
            new GuardianReviewWindow
            {
                DataContext =
                    viewModel
            };

        if (Application.Current.MainWindow is not null)
        {
            window.Owner =
                Application.Current.MainWindow;
        }

        window.ShowDialog();
    }

    // ============================================================
    // RESPONSE CONVERSION
    // ============================================================

    private static GuardianTaskActionPlan
        ConvertToTaskActionPlan(
            MeetingAnalysisResponse analysis)
    {
        ArgumentNullException.ThrowIfNull(
            analysis);

        var plan =
            new GuardianTaskActionPlan
            {
                Summary =
                    string.IsNullOrWhiteSpace(
                        analysis.Summary)
                        ? "Guardian analysed the meeting notes."
                        : analysis.Summary,

                Questions =
                    analysis.Questions
                        .Where(question =>
                            !string.IsNullOrWhiteSpace(
                                question))
                        .ToList()
            };

        foreach (var meetingAction in analysis.Actions)
        {
            plan.Actions.Add(
                ConvertAction(
                    meetingAction));
        }

        return plan;
    }

    private static GuardianTaskAction ConvertAction(
        MeetingAction action)
    {
        var title =
            BuildActionTitle(
                action);

        return new GuardianTaskAction
        {
            ActionType =
                NormaliseActionType(
                    action.ActionType),

            Title =
                title,

            Description =
                BuildActionDescription(
                    action),

            Reason =
                action.Reason,

            Category =
                action.TargetType,

            Priority =
                null,

            Confidence =
                CalculateConfidence(
                    action),

            ConfidenceReasons =
                BuildConfidenceReasons(
                    action),

            RequiresConfirmation =
                true,

            Selected =
                true
        };
    }

    private static string BuildActionTitle(
        MeetingAction action)
    {
        if (string.Equals(
                action.ActionType,
                MeetingActionType.Create,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(
                    action.TargetName)
                ? $"Create {action.TargetType}"
                : action.TargetName;
        }

        if (!string.IsNullOrWhiteSpace(
                action.TargetName))
        {
            return action.TargetName;
        }

        return $"{action.ActionType} {action.TargetType}";
    }

    private static string BuildActionDescription(
        MeetingAction action)
    {
        var parts =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                action.TargetType))
        {
            parts.Add(
                $"Target: {action.TargetType}");
        }

        if (!string.IsNullOrWhiteSpace(
                action.Property))
        {
            parts.Add(
                $"Change {action.Property}");
        }

        if (!string.IsNullOrWhiteSpace(
                action.NewValue))
        {
            parts.Add(
                $"New value: {action.NewValue}");
        }

        return parts.Count == 0
            ? "Guardian identified a proposed workspace change."
            : string.Join(
                ". ",
                parts) + ".";
    }

    private static string NormaliseActionType(
        string? actionType)
    {
        if (string.Equals(
                actionType,
                MeetingActionType.Create,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Add";
        }

        if (string.Equals(
                actionType,
                MeetingActionType.Update,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Update";
        }

        if (string.Equals(
                actionType,
                MeetingActionType.Delete,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Delete";
        }

        return string.IsNullOrWhiteSpace(
                actionType)
            ? "Review"
            : actionType;
    }

    // ============================================================
    // INITIAL CONFIDENCE ESTIMATION
    // ============================================================

    private static int CalculateConfidence(
        MeetingAction action)
    {
        var confidence =
            40;

        if (!string.IsNullOrWhiteSpace(
                action.ActionType))
        {
            confidence += 10;
        }

        if (!string.IsNullOrWhiteSpace(
                action.TargetType))
        {
            confidence += 10;
        }

        if (!string.IsNullOrWhiteSpace(
                action.TargetName))
        {
            confidence += 15;
        }

        if (!string.IsNullOrWhiteSpace(
                action.Reason))
        {
            confidence += 10;
        }

        if (string.Equals(
                action.ActionType,
                MeetingActionType.Update,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(
                    action.Property))
            {
                confidence += 5;
            }

            if (!string.IsNullOrWhiteSpace(
                    action.NewValue))
            {
                confidence += 5;
            }
        }

        return Math.Clamp(
            confidence,
            0,
            95);
    }

    private static List<string> BuildConfidenceReasons(
        MeetingAction action)
    {
        var reasons =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                action.TargetName))
        {
            reasons.Add(
                $"Target identified as '{action.TargetName}'.");
        }
        else
        {
            reasons.Add(
                "Guardian could not identify a specific target.");
        }

        if (!string.IsNullOrWhiteSpace(
                action.Property) &&
            !string.IsNullOrWhiteSpace(
                action.NewValue))
        {
            reasons.Add(
                $"Proposed change identified: " +
                $"{action.Property} → {action.NewValue}.");
        }

        if (!string.IsNullOrWhiteSpace(
                action.Reason))
        {
            reasons.Add(
                "A supporting reason was identified in the meeting notes.");
        }

        return reasons;
    }

    // ============================================================
    // STATUS
    // ============================================================

    private static string BuildStatusMessage(
        GuardianTaskActionPlan plan)
    {
        var actionCount =
            plan.Actions.Count;

        var questionCount =
            plan.Questions.Count;

        if (actionCount == 0 &&
            questionCount == 0)
        {
            return
                "Guardian did not identify any workspace changes.";
        }

        if (questionCount > 0)
        {
            return
                $"Guardian prepared {actionCount} proposed action" +
                $"{(actionCount == 1 ? "" : "s")} and needs clarification " +
                $"on {questionCount} item" +
                $"{(questionCount == 1 ? "" : "s")}.";
        }

        return
            $"Guardian prepared {actionCount} proposed action" +
            $"{(actionCount == 1 ? "" : "s")} for review.";
    }
}