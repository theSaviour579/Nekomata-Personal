namespace Nekomata.Models.Guardian;

public enum GuardianExceptionCategory
{
    DecisionRequired, ApprovalRequired, AmbiguousContext, MaterialPlanningChange,
    HighImpactDependency, ComplianceRisk, ActionFailed
}

public sealed class GuardianOperationalException
{
    public string Key { get; set; } = "";
    public GuardianExceptionCategory Category { get; set; }
    public string Severity { get; set; } = "Warning";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string ActionKind { get; set; } = "open_dashboard";
    public string ContextId { get; set; } = "";
    public Guid? WorkThreadId { get; set; }
}

public sealed class GuardianExceptionManagementResult
{
    public int RoutineActionsHandled { get; set; }
    public List<GuardianOperationalException> Exceptions { get; set; } = [];
    public string ActivitySummary => RoutineActionsHandled == 0 ? "No routine changes handled." :
        $"Guardian handled {RoutineActionsHandled} routine change{(RoutineActionsHandled == 1 ? "" : "s")}.";
}
