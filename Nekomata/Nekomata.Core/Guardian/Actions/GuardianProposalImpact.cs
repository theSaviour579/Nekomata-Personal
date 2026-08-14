using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public sealed record GuardianProposalImpact(
    int SelectedCount,
    int TotalCount,
    int NewTaskCount,
    int TaskChangeCount,
    int ProjectChangeCount,
    int CalendarChangeCount,
    int EstimatedMinutes,
    decimal EstimatedBusinessValue)
{
    public static GuardianProposalImpact From(GuardianActionResponse? proposal)
    {
        if (proposal is null)
            return new(0, 0, 0, 0, 0, 0, 0, 0);

        var tasks = proposal.Tasks.Where(item => item.Selected).ToList();
        var changes = proposal.Changes.Where(item => item.Selected).ToList();
        return new(
            tasks.Count + changes.Count,
            proposal.Tasks.Count + proposal.Changes.Count,
            tasks.Count,
            changes.Count(item => item.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase)),
            changes.Count(item => item.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase)),
            changes.Count(item => item.EntityType.Equals("Calendar", StringComparison.OrdinalIgnoreCase)),
            tasks.Sum(item => Math.Max(0, item.EstimatedMinutes)),
            tasks.Sum(item => Math.Max(0, item.EstimatedBusinessValue)));
    }
}
