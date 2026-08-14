using Nekomata.AI.Models.Actions;
using Nekomata.Data.Repositories;

namespace Nekomata.Core.Guardian.Changes;

public class GuardianProjectChangeHandler
    : IGuardianProjectChangeHandler
{
    private readonly IProjectRepository
        _projectRepository;

    private readonly IGuardianProjectChangeValidator
    _validator;

    public GuardianProjectChangeHandler(
        IProjectRepository projectRepository,
        IGuardianProjectChangeValidator validator)
    {
        _projectRepository =
            projectRepository;

        _validator =
            validator;
    }

    public async Task ApplyAsync(
       GuardianChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!_validator.CanApply(change))
        {
            return;
        }

        var project =
            await _projectRepository
                .GetByIdAsync(change.EntityId);

        if (project is null)
            return;

        switch (change.Property)
        {
            case "Priority":

                project.Priority =
                    change.NewValue;

                break;

            default:

                return;
        }

        await _projectRepository
            .SaveAsync(project);
    }
}