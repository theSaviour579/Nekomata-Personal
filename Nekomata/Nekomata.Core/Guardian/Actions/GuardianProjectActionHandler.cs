using Nekomata.AI.Models.Actions;
using Nekomata.Core.Guardian.Changes;
using Nekomata.Data.Repositories;
using System.Text.Json;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianProjectActionHandler
    : IGuardianProjectActionHandler
{
    private readonly IGuardianProjectChangeHandler
        _changeHandler;
    private readonly IProjectRepository _projectRepository;

    public GuardianProjectActionHandler(
        IGuardianProjectChangeHandler changeHandler,
        IProjectRepository projectRepository)
    {
        _changeHandler =
            changeHandler;
        _projectRepository = projectRepository;
    }

    public async Task ApplyAsync(
        GuardianActionResponse response,
        GuardianApplyResult result)
    {
        foreach (var change in
                 response.Changes.Where(c => c.Selected && c.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase)))
        {
            var before = await _projectRepository.GetByIdAsync(change.EntityId);
            await _changeHandler
                .ApplyAsync(change);
            var after = await _projectRepository.GetByIdAsync(change.EntityId);

            result.Actions.Add(
                new GuardianAppliedAction
                {
                    Type = "Project",

                    Title =
                        $"{change.Property}",

                    Description =
                        $"{change.OldValue} → {change.NewValue}",

                    EntityId =
                        change.EntityId,
                    Operation = "Update",
                    BeforeState = before is null ? null : JsonSerializer.Serialize(before),
                    AfterState = after is null ? null : JsonSerializer.Serialize(after),
                    Reversible = before is not null && after is not null,
                    IrreversibleReason = before is null || after is null ? "The project change could not be verified." : null,
                    Reason = change.Reason,
                    Confidence = change.Confidence
                });
        }
    }
}
