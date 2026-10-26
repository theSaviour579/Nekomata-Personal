namespace Nekomata.Models.Guardian;

public sealed class GuardianOutcomeSummary
{
    public int EvidenceCount { get; set; }
    public List<GuardianOutcomeInsight> Insights { get; set; } = [];
}

public sealed class GuardianOutcomeInsight
{
    public string Kind { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Explanation { get; set; } = "";
    public int SampleSize { get; set; }
    public int ConfidencePercent { get; set; }
    public double? ObservedRatio { get; set; }
    public int? PreferredStartHour { get; set; }
    public string PolicyAction { get; set; } = "";
    public bool IsPolicyActive { get; set; }
    public double? AppliedFactor { get; set; }
}
