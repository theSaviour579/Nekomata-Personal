namespace Nekomata.Models.Guardian;

public sealed class GuardianExecutiveBriefing
{
    public DateTimeOffset GeneratedAt { get; set; }
    public string Narrative { get; set; } = "";
    public int DecisionCount { get; set; }
    public string DecisionSummary { get; set; } = "";
    public string HandledActivitySummary { get; set; } = "";
    public string ScheduleSummary { get; set; } = "";
    public string DependencySummary { get; set; } = "";
    public string ComplianceSummary { get; set; } = "";
    public string PreparedActionSummary { get; set; } = "";
    public string AnticipationSummary { get; set; } = "";
    public string CapacitySummary { get; set; } = "";
    public List<GuardianBriefingProvenance> Provenance { get; set; } = [];
}

public sealed record GuardianBriefingProvenance(
    string SourceType,
    string Label,
    Guid? SourceEventId = null,
    Guid? WorkThreadId = null);
