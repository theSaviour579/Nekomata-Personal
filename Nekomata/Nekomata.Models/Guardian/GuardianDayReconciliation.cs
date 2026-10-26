namespace Nekomata.Models.Guardian;

public enum GuardianReconciliationChangeKind { Routine, Exception, Material }

public sealed class GuardianReconciliationChange
{
    public GuardianReconciliationChangeKind Kind { get; set; }
    public string ChangeType { get; set; } = "";
    public string Summary { get; set; } = "";
    public Guid? WorkThreadId { get; set; }
    public long? TaskId { get; set; }
}

public sealed class GuardianDayReconciliationResult
{
    public DateTimeOffset ReconciledAt { get; set; }
    public List<GuardianReconciliationChange> Changes { get; set; } = [];
    public int RoutineCount => Changes.Count(x => x.Kind == GuardianReconciliationChangeKind.Routine);
    public int ExceptionCount => Changes.Count(x => x.Kind == GuardianReconciliationChangeKind.Exception);
    public int MaterialCount => Changes.Count(x => x.Kind == GuardianReconciliationChangeKind.Material);
    public bool RequiresAttention => ExceptionCount + MaterialCount > 0;
    public string Summary => Changes.Count == 0 ? "No meaningful operational changes detected." :
        $"Guardian reconciled {Changes.Count} change(s): {RoutineCount} routine, {ExceptionCount} exception, {MaterialCount} material.";
}
