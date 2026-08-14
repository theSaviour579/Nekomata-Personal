using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Changes;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianProjectActionHandler
    : IGuardianProjectActionHandler
{
    private readonly IGuardianProjectChangeHandler
        _changeHandler;

    public GuardianProjectActionHandler(
        IGuardianProjectChangeHandler changeHandler)
    {
        _changeHandler =
            changeHandler;
    }

    public async Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result)
    {
        foreach (var change in
                 response.Changes.Where(c => c.Selected && c.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase)))
        {
            await _changeHandler
                .ApplyAsync(change);

            result.Actions.Add(
                new GuardianAppliedAction
                {
                    Type = "Project",

                    Title =
                        $"{change.Property}",

                    Description =
                        $"{change.OldValue} → {change.NewValue}",

                    EntityId =
                        change.EntityId
                });
        }
    }
}