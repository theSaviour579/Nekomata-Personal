using Nekomata.Models.Common;
using Nekomata.Models.Guardian;
using Nekomata.Models.Missions;
using Nekomata.Models.Workspace;

namespace Nekomata.Core.Missions.Suggestions.Rules;

public class MissingDueDatesSuggestionRule
    : ISuggestedMissionRule
{
    private const int MinimumMissingDueDates = 5;

    public string Name =>
        "Missing Due Dates";

    public IReadOnlyList<GuardianInsight> Evaluate(
        SuggestedMissionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var missingDueDateCount =
            CountTasksWithoutDueDates(
                context.Workspace);

        var suggestionAlreadyExists =
    context.Workspace.Tasks.Any(task =>
        string.Equals(
            task.Source,
            "Guardian",
            StringComparison.OrdinalIgnoreCase)
        &&
        string.Equals(
            task.Title,
            "Assign due dates to outstanding tasks",
            StringComparison.OrdinalIgnoreCase)
        &&
        !task.IsCompleted);

        if (suggestionAlreadyExists)
        {
            return [];
        }

        if (missingDueDateCount <
            MinimumMissingDueDates)
        {
            return [];
        }

        var mission =
            new MissionCandidate
            {
                SourceType =
                    "Guardian",

                SourceRecordId =
                    "guardian:missing-due-dates",

                Title =
                    "Assign due dates to outstanding tasks",

                Description =
                    $"{missingDueDateCount} active tasks have no due date.",

                Priority =
                    TaskPriorities.Normal,

                BaseScore =
                    35,

                EstimatedMinutes =
                    Math.Clamp(
                        missingDueDateCount * 2,
                        15,
                        60),

                BusinessValue =
                    0,

                AtRisk =
                    missingDueDateCount >= 15
            };

        return
        [
            new GuardianInsight
            {
                Id =
                    "guardian:missing-due-dates",

                Title =
                    $"{missingDueDateCount} tasks have no due date",

                Description =
                    "Assigning realistic due dates will improve Guardian’s " +
                    "planning, urgency scoring and timeline accuracy.",

                Category =
                    "Planning",

                Severity =
                    missingDueDateCount >= 15
                        ? "Warning"
                        : "Info",

                SourceType =
                    "Guardian",

                DetectedAt =
                    context.Now,

                CanCreateMission =
                    true,

                SuggestedMission =
                    mission
            }
        ];
    }

    private static int CountTasksWithoutDueDates(
        NekomataWorkspace workspace)
    {
        return workspace.Tasks
            .Count(task =>
                task.DueAt is null &&
                !IsCompleted(task.Status));
    }

    private static bool IsCompleted(
        string? status)
    {
        return string.Equals(
                   status,
                   "Completed",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   status,
                   "Closed",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   status,
                   "Cancelled",
                   StringComparison.OrdinalIgnoreCase);
    }
}