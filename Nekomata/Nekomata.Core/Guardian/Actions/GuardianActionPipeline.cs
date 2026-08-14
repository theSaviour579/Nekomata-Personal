using Nekomata.AI.Models.Actions;

namespace Nekomata.Core.Guardian.Actions;

public class GuardianActionPipeline : IGuardianActionPipeline
{
    private readonly IGuardianTaskActionHandler _taskHandler;
    private readonly IGuardianProjectActionHandler _projectHandler;
    private readonly IGuardianCalendarActionHandler _calendarHandler;

    public GuardianActionPipeline(
        IGuardianTaskActionHandler taskHandler,
        IGuardianProjectActionHandler projectHandler,
        IGuardianCalendarActionHandler calendarHandler)
    {
        _taskHandler = taskHandler;
        _projectHandler = projectHandler;
        _calendarHandler = calendarHandler;
    }

    public async Task ExecuteAsync(GuardianActionResponse response, GuardianApplyResult result)
    {
        await _taskHandler.ApplyAsync(response, result);
        await _projectHandler.ApplyAsync(response, result);
        await _calendarHandler.ApplyAsync(response, result);
    }
}
