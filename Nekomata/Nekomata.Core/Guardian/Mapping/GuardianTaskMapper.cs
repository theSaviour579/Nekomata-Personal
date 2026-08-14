using Nekomata.AI.Models.Actions;
using Nekomata.Models.Tasks;

namespace Nekomata.Core.Guardian.Mapping;

public class GuardianTaskMapper
    : IGuardianTaskMapper
{
    public NekomataTask Map(
        ProposedTask proposedTask,
        long? projectId)
    {
        ArgumentNullException.ThrowIfNull(
            proposedTask);

        return new NekomataTask
        {
            ProjectId = projectId,

            Title = proposedTask.Title,

            Description = proposedTask.Description,

            Priority = proposedTask.Priority,

            EstimatedMinutes =
                proposedTask.EstimatedMinutes,

            EstimatedBusinessValue =
                proposedTask.EstimatedBusinessValue,

            RequiresSql =
                proposedTask.RequiresSql,

            RequiresFocus =
                proposedTask.RequiresFocus,

            SuggestedDelegate =
                proposedTask.SuggestedDelegate,

            Source = "Guardian",

            Status = "Open",

            Owner = "David",

            BusinessCritical = false,
            AccuracySensitive = false,
            Interruptible = false,
            Recurring = false,

            RevenueImpact = 0,
            CustomerImpact = 0,
            ExecutiveVisibility = 0,

            AutomationPotential = 0,

            PriorityScore = 0
        };
    }
}