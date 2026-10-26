namespace Nekomata.Models.Guardian;

public sealed record GuardianOpenLoopChase(
    GuardianOpenLoop OpenLoop,
    int WorkingDaysOpen,
    bool ChaseMature,
    string Message,
    GuardianChaseState ChaseState = GuardianChaseState.NotDue,
    DateTimeOffset? NextChaseDueAt = null,
    int ChaseCount = 0);
